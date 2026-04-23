using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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

var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Jwt:Key missing");

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
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
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

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// Single RPC entrypoint.
app.MapPost("/rpc", async (HttpContext ctx, RpcRouter router) => await router.HandleAsync(ctx));

// Static media (WebP images).
var mediaPath = Path.Combine(app.Environment.ContentRootPath, "media");
Directory.CreateDirectory(mediaPath);
app.UseStaticFiles(new Microsoft.AspNetCore.Builder.StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(mediaPath),
    RequestPath = "/media"
});

app.Run();
