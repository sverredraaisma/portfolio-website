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
    //
    // Username + email come from config so a fork doesn't silently seed
    // an account in the original owner's name. Defaults match the owner's
    // own deploy so the existing setup keeps booting unchanged.
    var seedUsername = builder.Configuration["Admin:SeedUsername"] ?? "opperautist";
    var seedEmail    = builder.Configuration["Admin:SeedEmail"]    ?? "sverre@draaisma.dev";
    var seeded = await auth.SeedAdminIfEmptyAsync(seedUsername, seedEmail);
    if (seeded)
    {
        log.LogInformation(
            "Seeded admin account '{Username}' ({Email}). " +
            "Use the password-reset flow against that email to claim it.",
            seedUsername, seedEmail);
    }
}

// Apply X-Forwarded-* before anything that reads RemoteIpAddress or Scheme.
app.UseForwardedHeaders();

// Basic security headers on every response. nginx layers an identical CSP
// on the public origin; setting it here too means a direct-to-backend hit
// (dev mode without the proxy, or future operator tooling) is also covered.
//
// 'unsafe-inline' on script-src + style-src is reluctant — Nuxt's SSR
// emits an inline <script> with the hydration payload and Vue scoped
// styles get inlined as <style>. A nonce-based policy needs nuxt-security
// or hand-rolled SSR hooks. The tighter directives still close
// frame-ancestors / object-src / base-uri / form-action.
const string Csp =
    "default-src 'self'; " +
    "script-src 'self' 'unsafe-inline'; " +
    "style-src 'self' 'unsafe-inline'; " +
    // tile.openstreetmap.org (a/b/c subdomains) serves the Leaflet basemap
    // tiles for /map. Without this, the map renders an empty grey grid.
    "img-src 'self' data: blob: https://*.tile.openstreetmap.org; " +
    "font-src 'self'; " +
    "connect-src 'self'; " +
    "frame-ancestors 'none'; " +
    "base-uri 'self'; " +
    "form-action 'self'; " +
    "object-src 'none'";

app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "DENY";
    h["Referrer-Policy"] = "no-referrer";
    // geolocation=(self) so the /account "share my browser location" button
    // can call navigator.geolocation. microphone/camera stay disabled — no
    // feature on the site needs them.
    h["Permissions-Policy"] = "geolocation=(self), microphone=(), camera=()";
    h["Cross-Origin-Opener-Policy"] = "same-origin";
    h["Cross-Origin-Resource-Policy"] = "same-origin";
    h["Content-Security-Policy"] = Csp;
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
