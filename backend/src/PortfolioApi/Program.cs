using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using PortfolioApi.Configuration;
using PortfolioApi.Data;
using PortfolioApi.Extensions;
using PortfolioApi.Rpc;
using PortfolioApi.Services;

var builder = WebApplication.CreateBuilder(args);

// Behind nginx in compose: trust X-Forwarded-* so RemoteIpAddress (used by the
// rate limiter) is the real client, not nginx's container IP. Lists are cleared
// because the proxy is on a docker bridge network — its IP isn't a known one
// to ASP.NET, and the backend isn't directly reachable from the host anyway.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
});

// Bind + validate options first so the rest of the wiring can rely on
// JwtOptions/EmailOptions/etc being present and well-formed.
builder.Services.AddPortfolioOptions(builder.Configuration);

builder.Services.AddPortfolioServices(builder.Configuration);
builder.Services.AddPortfolioJwt(builder.Configuration);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(builder.Configuration["Cors:FrontendOrigin"] ?? "http://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials());
});

// Cap request bodies. RPC payloads should be small; image uploads have their own
// stricter cap inside PostMethods.UploadImage.
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = 8 * 1024 * 1024;
});
builder.WebHost.ConfigureKestrel(o =>
{
    o.Limits.MaxRequestBodySize = 8 * 1024 * 1024; // 8 MiB
});

// Rate limiting. Pass the options in directly so the partition factory captures
// concrete values once instead of resolving IOptions per request.
var rateLimits = builder.Configuration.GetSection(RateLimitingOptions.Section).Get<RateLimitingOptions>()
                 ?? new RateLimitingOptions();
builder.Services.AddPortfolioRateLimiting(rateLimits);

var app = builder.Build();

// Apply any pending EF Core migrations on startup.
// Postgres readiness is handled by the docker compose healthcheck.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
    var log = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    log.LogInformation("Applying database migrations...");
    db.Database.Migrate();
    log.LogInformation("Database migrations applied.");

    // First-boot bootstrap: if there are no accounts at all, seed the owner.
    // The password is random and never disclosed; the owner uses the password
    // reset flow against the configured email to gain access.
    var seeded = await auth.SeedAdminIfEmptyAsync(
        username: "opperautist",
        email: "sverre@draaisma.dev");
    if (seeded)
    {
        log.LogInformation(
            "Seeded admin account 'opperautist' (sverre@draaisma.dev). " +
            "Use the password-reset flow against that email to claim it.");
    }
}

// Apply X-Forwarded-* before anything that reads RemoteIpAddress or Scheme.
app.UseForwardedHeaders();

// Basic security headers on every response. CSP is intentionally relaxed:
// the SPA bundles its JS inline at runtime in places (Nuxt's hydration
// payload), so a strict policy needs nonces or hashes to stay compatible.
// The directives here cover the most common XSS / clickjacking vectors
// without breaking the Nuxt runtime; a stricter policy can be layered on
// top by the reverse proxy if needed.
app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "DENY";
    h["Referrer-Policy"] = "no-referrer";
    h["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";
    h["Cross-Origin-Opener-Policy"] = "same-origin";
    h["Cross-Origin-Resource-Policy"] = "same-origin";
    await next();
});

// Liveness probe. Cheap, no DB hit — answers "is the process up?". Compose
// healthchecks and external monitors can poll this without rate limits or
// auth in their way. /health is kept as an alias for backward compatibility.
app.MapGet("/health",      () => Results.Ok(new { status = "ok" }));
app.MapGet("/health/live", () => Results.Ok(new { status = "ok" }));

// Readiness probe. Verifies the DB is reachable so an orchestrator can
// withhold traffic until the backing service is actually usable. Returns
// 503 if the connection check fails — *not* 500, so a load balancer
// understands the instance should be excluded but doesn't need restarting.
app.MapGet("/health/ready", async (PortfolioApi.Data.AppDbContext db, CancellationToken ct) =>
{
    try
    {
        var ok = await db.Database.CanConnectAsync(ct);
        return ok
            ? Results.Ok(new { status = "ready" })
            : Results.Json(new { status = "db_unavailable" }, statusCode: 503);
    }
    catch (Exception ex)
    {
        return Results.Json(new { status = "db_error", error = ex.GetType().Name }, statusCode: 503);
    }
});

app.UseRateLimiter();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// Single RPC entrypoint. Rate limited by the global limiter above.
app.MapPost("/rpc", async (HttpContext ctx, RpcRouter router) => await router.HandleAsync(ctx));

// Public RSS feed of published posts (subscribe-without-an-account). Served
// outside the RPC router because feed readers expect a plain XML GET.
app.MapRss();

// Static media (WebP images). Read MediaRoot off IImageService so the path is
// single-sourced — the service has already created the directory.
var images = app.Services.GetRequiredService<IImageService>();
app.UseStaticFiles(new Microsoft.AspNetCore.Builder.StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(images.MediaRoot),
    RequestPath = "/media",
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers["Cache-Control"] = "public, max-age=31536000, immutable";
        ctx.Context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    }
});

app.Run();

// Top-level statements compile into an implicit `internal Program` class.
// Re-declare it as public partial so WebApplicationFactory<Program> in the
// test project can find a usable type symbol — Microsoft's standard pattern
// for ASP.NET Core 6+ in-process integration tests.
public partial class Program { }
