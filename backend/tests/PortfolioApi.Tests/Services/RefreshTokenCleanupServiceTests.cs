using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PortfolioApi.Data;
using PortfolioApi.Models;
using PortfolioApi.Services;
using PortfolioApi.Tests.Infrastructure;

namespace PortfolioApi.Tests.Services;

public class RefreshTokenCleanupServiceTests
{
    /// Wires the service against a single TestDb. Because the sweeper opens
    /// its own scope via IServiceProvider, we register the *same* context
    /// instance as a singleton so the test can also seed/inspect it.
    private static (RefreshTokenCleanupService sut, TestDb test) Build()
    {
        var test = new TestDb();
        var services = new ServiceCollection();
        services.AddSingleton(test.Db);
        return (new RefreshTokenCleanupService(services.BuildServiceProvider(),
                                              NullLogger<RefreshTokenCleanupService>.Instance),
                test);
    }

    private static User SeedUser(AppDbContext db)
    {
        var u = new User { Username = "alice", Email = "a@x", PasswordHash = new byte[]{1}, PasswordSalt = new byte[]{1} };
        db.Users.Add(u);
        db.SaveChanges();
        db.ChangeTracker.Clear();
        return u;
    }

    [Fact]
    public async Task Sweep_deletes_expired_tokens()
    {
        var (sut, test) = Build();
        var user = SeedUser(test.Db);
        test.Db.RefreshTokens.Add(new RefreshToken { UserId = user.Id, TokenHash = new byte[]{1}, ExpiresAt = DateTime.UtcNow.AddDays(-1) });
        await test.Db.SaveChangesAsync();
        test.Db.ChangeTracker.Clear();

        var deleted = await sut.SweepOnceAsync(default);

        deleted.Should().Be(1);
        (await test.Db.RefreshTokens.AnyAsync()).Should().BeFalse();
        test.Dispose();
    }

    [Fact]
    public async Task Sweep_keeps_active_unrevoked_tokens()
    {
        var (sut, test) = Build();
        var user = SeedUser(test.Db);
        test.Db.RefreshTokens.Add(new RefreshToken { UserId = user.Id, TokenHash = new byte[]{2}, ExpiresAt = DateTime.UtcNow.AddDays(7) });
        await test.Db.SaveChangesAsync();
        test.Db.ChangeTracker.Clear();

        var deleted = await sut.SweepOnceAsync(default);

        deleted.Should().Be(0);
        (await test.Db.RefreshTokens.CountAsync()).Should().Be(1);
        test.Dispose();
    }

    [Fact]
    public async Task Sweep_keeps_recently_revoked_tokens_inside_the_audit_window()
    {
        // Revoked an hour ago — the audit window is 7 days, so it stays.
        var (sut, test) = Build();
        var user = SeedUser(test.Db);
        test.Db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id, TokenHash = new byte[]{3},
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            RevokedAt = DateTime.UtcNow.AddHours(-1)
        });
        await test.Db.SaveChangesAsync();
        test.Db.ChangeTracker.Clear();

        var deleted = await sut.SweepOnceAsync(default);

        deleted.Should().Be(0);
        test.Dispose();
    }

    [Fact]
    public async Task Sweep_deletes_revoked_tokens_older_than_the_audit_window()
    {
        var (sut, test) = Build();
        var user = SeedUser(test.Db);
        test.Db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id, TokenHash = new byte[]{4},
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            RevokedAt = DateTime.UtcNow.AddDays(-30)
        });
        await test.Db.SaveChangesAsync();
        test.Db.ChangeTracker.Clear();

        var deleted = await sut.SweepOnceAsync(default);

        deleted.Should().Be(1);
        test.Dispose();
    }
}
