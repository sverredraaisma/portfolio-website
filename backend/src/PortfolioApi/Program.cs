using Microsoft.EntityFrameworkCore;
using PortfolioApi.Configuration;
using PortfolioApi.Data;
using PortfolioApi.Extensions;
using PortfolioApi.Rpc;
using PortfolioApi.Services;

var builder = WebApplication.CreateBuilder(args);

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
    var log = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    log.LogInformation("Applying database migrations...");
    db.Database.Migrate();
    log.LogInformation("Database migrations applied.");
}

// Basic security headers on every response.
app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "DENY";
    h["Referrer-Policy"] = "no-referrer";
    h["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";
    await next();
});

app.UseRateLimiter();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// Single RPC entrypoint. Rate limited by the global limiter above.
app.MapPost("/rpc", async (HttpContext ctx, RpcRouter router) => await router.HandleAsync(ctx));

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
