using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PortfolioApi.Configuration;
using PortfolioApi.Data;
using PortfolioApi.Rpc;
using PortfolioApi.Rpc.Methods;
using PortfolioApi.Services;

namespace PortfolioApi.Extensions;

/// Wiring helpers for Program.cs. Keeping the registrations grouped here keeps
/// Program.cs short and makes the option-binding pattern (Bind +
/// ValidateDataAnnotations + ValidateOnStart) consistent across sections.
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPortfolioOptions(this IServiceCollection services, IConfiguration cfg)
    {
        // JwtOptions is the only section we *must* fail-fast on — a missing or
        // short signing key would happily let the app start and then 500 on
        // every login, which is worse than refusing to boot.
        services.AddOptions<JwtOptions>()
            .Bind(cfg.GetSection(JwtOptions.Section))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<EmailOptions>()
            .Bind(cfg.GetSection(EmailOptions.Section));

        services.AddOptions<ImageOptions>()
            .Bind(cfg.GetSection(ImageOptions.Section));

        services.AddOptions<RateLimitingOptions>()
            .Bind(cfg.GetSection(RateLimitingOptions.Section));

        return services;
    }

    public static IServiceCollection AddPortfolioServices(this IServiceCollection services, IConfiguration cfg)
    {
        services.AddDbContext<AppDbContext>(opt =>
            opt.UseNpgsql(cfg.GetConnectionString("Postgres")));

        services.AddSingleton<IJwtService, JwtService>();
        services.AddSingleton<IEmailService, EmailService>();
        services.AddSingleton<IImageService, ImageService>();
        services.AddScoped<IAuthService, AuthService>();

        services.AddScoped<RpcRouter>();
        services.AddScoped<AuthMethods>();
        services.AddScoped<PostMethods>();
        services.AddScoped<CommentMethods>();

        return services;
    }

    public static IServiceCollection AddPortfolioJwt(this IServiceCollection services, IConfiguration cfg)
    {
        // Read once for the bearer middleware. Validation of the values themselves
        // happens in AddPortfolioOptions.ValidateOnStart; here we only need the
        // bytes for the symmetric key.
        var jwt = cfg.GetSection(JwtOptions.Section).Get<JwtOptions>()
            ?? throw new InvalidOperationException("Jwt configuration section is missing");

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key));

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(opt =>
            {
                // Don't translate "sub"/"email" into the long XML-schema claim URIs.
                // We look up claims by their raw JWT names elsewhere (RpcContext.UserId
                // reads "sub" directly), so the default mapping just makes that fail
                // silently.
                opt.MapInboundClaims = false;

                opt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = key,
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
            });

        services.AddAuthorization();

        return services;
    }

    public static IServiceCollection AddPortfolioRateLimiting(this IServiceCollection services, RateLimitingOptions rl)
    {
        // Capture the option values directly so the partition factory doesn't
        // need to resolve IOptions on every request. Changing the limit
        // therefore requires a restart — fine for our deployment model.
        var permit = rl.PermitLimit;
        var window = TimeSpan.FromSeconds(rl.WindowSeconds);

        services.AddRateLimiter(opt =>
        {
            opt.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            opt.GlobalLimiter = System.Threading.RateLimiting.PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
                    {
                        PermitLimit = permit,
                        Window = window,
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));
        });

        return services;
    }
}
