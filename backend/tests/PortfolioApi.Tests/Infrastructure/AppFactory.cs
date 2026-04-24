using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PortfolioApi.Data;
using PortfolioApi.Services;

namespace PortfolioApi.Tests.Infrastructure;

/// In-process WebApplicationFactory that boots the real Program.cs pipeline
/// (auth, RPC router, security headers, the lot) over a SQLite in-memory
/// database and a stubbed-out email service. Lets tests hit /rpc with a
/// real HttpClient and assert on the wire-level shape.
///
/// The SQLite connection is held by the factory so the in-memory DB lives
/// for the lifetime of the test class.
public sealed class AppFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _conn = new("DataSource=:memory:");
    public RecordingEmail Email { get; } = new();

    public AppFactory()
    {
        _conn.Open();
        // Program.cs reads JwtOptions during AddPortfolioJwt, which runs
        // before WebApplicationFactory's ConfigureAppConfiguration overrides
        // are visible. Setting the env vars here makes them readable from
        // the very first configuration build.
        Environment.SetEnvironmentVariable("Jwt__Issuer", "test-issuer");
        Environment.SetEnvironmentVariable("Jwt__Audience", "test-audience");
        Environment.SetEnvironmentVariable("Jwt__Key", "test-key-with-more-than-thirty-two-characters-please");
        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres",
            "Host=localhost;Database=fake;Username=fake;Password=fake");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // Provide the bare minimum the bound options need: JwtOptions
        // ValidateOnStart will refuse to boot without a >= 32 byte key.
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "test-issuer",
                ["Jwt:Audience"] = "test-audience",
                ["Jwt:Key"] = "test-key-with-more-than-thirty-two-characters-please",
                // ConnectionStrings:Postgres is read by AddDbContext; we
                // override the registration below, but a non-empty string
                // here keeps the configuration validators happy.
                ["ConnectionStrings:Postgres"] = "Host=localhost;Database=fake;Username=fake;Password=fake"
            });
        });

        builder.ConfigureServices(services =>
        {
            // Swap the DbContext registration to SQLite + a shared connection
            // so EnsureCreated builds the schema once and tests share it.
            var dbDescriptor = services.Single(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            services.Remove(dbDescriptor);
            services.AddDbContext<AppDbContext>(o => o.UseSqlite(_conn));

            // Replace the SMTP-backed email service with a recording stub so
            // we can inspect outbound mail and avoid any network attempt.
            var emailDescriptor = services.Single(d => d.ServiceType == typeof(IEmailService));
            services.Remove(emailDescriptor);
            services.AddSingleton<IEmailService>(Email);

            // Drop the background services (refresh-token sweeper, signing
            // keypair init etc) — they aren't needed for the request-level
            // tests and would add noise to the boot output.
            foreach (var hosted in services
                         .Where(d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService))
                         .ToList())
                services.Remove(hosted);
        });

        base.ConfigureWebHost(builder);
    }

    /// EnsureCreated runs the model build against SQLite (which our
    /// provider-aware OnModelCreating handles). Called explicitly by tests
    /// so they can also seed before the first request.
    public async Task ResetDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _conn.Dispose();
        base.Dispose(disposing);
    }

    public sealed class RecordingEmail : IEmailService
    {
        public List<(string To, string Token)> Verifications { get; } = new();
        public List<(string To, string Token)> Resets { get; } = new();
        public List<(string To, string Token)> EmailChanges { get; } = new();
        public List<(string To, string Action)> Alerts { get; } = new();

        public Task SendVerificationAsync(string to, string t) { Verifications.Add((to, t)); return Task.CompletedTask; }
        public Task SendPasswordResetAsync(string to, string t) { Resets.Add((to, t)); return Task.CompletedTask; }
        public Task SendEmailChangeAsync(string to, string t)   { EmailChanges.Add((to, t)); return Task.CompletedTask; }
        public Task SendSecurityAlertAsync(string to, string label, string? note = null)
        { Alerts.Add((to, label)); return Task.CompletedTask; }
    }
}
