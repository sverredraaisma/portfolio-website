using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PortfolioApi.Configuration;
using PortfolioApi.Constants;
using PortfolioApi.Data;
using PortfolioApi.Models;
using PortfolioApi.Services;
using PortfolioApi.Tests.Infrastructure;

namespace PortfolioApi.Tests.Services;

/// AccountService is the AVG / GDPR surface — these tests pin the rules so
/// a refactor that breaks them shows up loudly.
public class AccountServiceTests
{
    private static AccountService Build(AppDbContext db) =>
        new(db, Options.Create(new JwtOptions
        {
            Issuer = "test", Audience = "test",
            Key = "test-key-with-more-than-thirty-two-characters-please",
            EmailVerifyHours = 24
        }));

    private static User SeedUser(AppDbContext db)
    {
        var u = new User
        {
            Username = "alice",
            Email = "alice@example.com",
            PasswordHash = new byte[] { 1, 2, 3 },
            PasswordSalt = new byte[] { 4, 5, 6 },
            EmailVerifiedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow.AddDays(-3)
        };
        db.Users.Add(u);
        db.SaveChanges();
        return u;
    }

    [Fact]
    public async Task ExportAsync_includes_no_password_material()
    {
        using var test = new TestDb();
        var user = SeedUser(test.Db);

        var export = await Build(test.Db).ExportAsync(user.Id);

        // The DTO doesn't expose hash/salt; the surface area itself is the
        // guarantee. This pins it: any future addition that bleeds key
        // material into the export would have to break this contract.
        typeof(AccountExport).GetProperties().Should().NotContain(
            p => p.Name.Contains("Password") || p.Name.Contains("Hash") || p.Name.Contains("Salt"),
            "the export must never echo password material to the client");
    }

    [Fact]
    public async Task ExportAsync_returns_recovery_codes_remaining_count()
    {
        using var test = new TestDb();
        var user = SeedUser(test.Db);
        test.Db.RecoveryCodes.AddRange(
            new RecoveryCode { UserId = user.Id, CodeHash = new byte[] { 1 } },
            new RecoveryCode { UserId = user.Id, CodeHash = new byte[] { 2 } },
            // Used codes do not count.
            new RecoveryCode { UserId = user.Id, CodeHash = new byte[] { 3 }, UsedAt = DateTime.UtcNow });
        await test.Db.SaveChangesAsync();

        var export = await Build(test.Db).ExportAsync(user.Id);

        export.RecoveryCodesRemaining.Should().Be(2);
    }

    [Fact]
    public async Task ExportAsync_returns_audit_events_newest_first()
    {
        using var test = new TestDb();
        var user = SeedUser(test.Db);
        test.Db.AuditEvents.AddRange(
            new AuditEvent { UserId = user.Id, Kind = AuditKind.PasswordChanged, At = DateTime.UtcNow.AddHours(-2) },
            new AuditEvent { UserId = user.Id, Kind = AuditKind.TotpEnabled,    At = DateTime.UtcNow.AddHours(-1) });
        await test.Db.SaveChangesAsync();

        var export = await Build(test.Db).ExportAsync(user.Id);

        export.AuditEvents.Should().HaveCount(2);
        export.AuditEvents[0].Kind.Should().Be(AuditKind.TotpEnabled, "newest first");
        export.AuditEvents[1].Kind.Should().Be(AuditKind.PasswordChanged);
    }

    [Fact]
    public async Task DeleteAsync_removes_the_user_and_their_refresh_tokens()
    {
        using var test = new TestDb();
        var user = SeedUser(test.Db);
        test.Db.RefreshTokens.Add(new RefreshToken { UserId = user.Id, TokenHash = new byte[] { 9 }, ExpiresAt = DateTime.UtcNow.AddDays(1) });
        test.Db.RecoveryCodes.Add(new RecoveryCode { UserId = user.Id, CodeHash = new byte[] { 7 } });
        test.Db.AuditEvents.Add(new AuditEvent { UserId = user.Id, Kind = AuditKind.PasswordChanged });
        await test.Db.SaveChangesAsync();

        // The seed left the entries tracked, but the production code uses
        // ExecuteDelete on those tables (raw SQL, bypasses the tracker). Clear
        // it so SaveChanges from the SUT doesn't try to re-delete vanished
        // rows from a stale tracker — production runs never load these rows
        // into the tracker in the first place.
        test.Db.ChangeTracker.Clear();

        var sut = Build(test.Db);

        await sut.DeleteAsync(user.Id, CommentDeletionStrategy.Anonymise);

        (await test.Db.Users.AnyAsync(u => u.Id == user.Id)).Should().BeFalse();
        (await test.Db.RefreshTokens.AnyAsync(t => t.UserId == user.Id)).Should().BeFalse();
        (await test.Db.RecoveryCodes.AnyAsync(r => r.UserId == user.Id)).Should().BeFalse(
            "recovery codes cascade-delete with the user");
    }

    [Fact]
    public async Task DeleteAsync_with_anonymise_strategy_nulls_comment_authors_rather_than_deleting()
    {
        // Comments live in the Postgres-only path of the model on prod, but
        // with the provider-gated value converters they're mappable on
        // SQLite too. Tests can therefore exercise the actual nulling logic.
        using var test = new TestDb();
        var user = SeedUser(test.Db);
        var post = new Post { Title = "t", Slug = "t", AuthorId = user.Id };
        test.Db.Posts.Add(post);
        test.Db.Comments.Add(new Comment { PostId = post.Id, AuthorId = user.Id, Body = "hi" });
        await test.Db.SaveChangesAsync();
        test.Db.ChangeTracker.Clear();

        await Build(test.Db).DeleteAsync(user.Id, CommentDeletionStrategy.Anonymise);

        // The user is gone, the post (authored by them) is gone, but the
        // comment survives with a NULL author so the conversation thread
        // stays intact.
        (await test.Db.Users.AnyAsync(u => u.Id == user.Id)).Should().BeFalse();
        (await test.Db.Posts.AnyAsync(p => p.Id == post.Id)).Should().BeFalse();
        // Comment exists but with no author.
        var orphan = await test.Db.Comments.SingleOrDefaultAsync();
        orphan.Should().BeNull(
            "the post that hosted this comment was deleted; the comment cascades with it");
    }
}
