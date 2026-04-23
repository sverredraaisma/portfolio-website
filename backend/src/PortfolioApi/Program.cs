using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PortfolioApi.Data;
using PortfolioApi.Rpc;
using PortfolioApi.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddSingleton<IJwtService, JwtService>();
builder.Services.AddSingleton<IEmailService, EmailService>();
builder.Services.AddSingleton<IImageService, ImageService>();
builder.Services.AddScoped<IAuthService, AuthService>();

builder.Services.AddScoped<RpcRouter>();
builder.Services.AddScoped<PortfolioApi.Rpc.Methods.AuthMethods>();
builder.Services.AddScoped<PortfolioApi.Rpc.Methods.PostMethods>();
builder.Services.AddScoped<PortfolioApi.Rpc.Methods.CommentMethods>();

// JWT signing key must be supplied via configuration (env / user-secrets / vault).
// Refuse to start if it's missing or shorter than 32 bytes — short keys defeat HS256.
var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey) || Encoding.UTF8.GetByteCount(jwtKey) < 32)
{
    throw new InvalidOperationException(
        "Jwt:Key is missing or too short (need >= 32 bytes). " +
        "Set it via env var Jwt__Key or user-secrets — never commit it.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorization();

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

// Rate limiting. Per-IP fixed window. Tight on /rpc; auth.* methods get an
// additional inner check inside RpcRouter.
builder.Services.AddRateLimiter(opt =>
{
    opt.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    opt.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

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

// Static media (WebP images).
var mediaPath = Path.Combine(app.Environment.ContentRootPath, "media");
Directory.CreateDirectory(mediaPath);
app.UseStaticFiles(new Microsoft.AspNetCore.Builder.StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(mediaPath),
    RequestPath = "/media",
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers["Cache-Control"] = "public, max-age=31536000, immutable";
        ctx.Context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    }
});

app.Run();
