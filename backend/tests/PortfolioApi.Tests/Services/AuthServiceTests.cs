using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PortfolioApi.Configuration;
using PortfolioApi.Data;
using PortfolioApi.Models;
using PortfolioApi.Services;
using PortfolioApi.Tests.Infrastructure;

namespace PortfolioApi.Tests.Services;

public class AuthServiceTests
{
    private static (AuthService sut, AppDbContext db, RecordingEmail email, IJwtService jwt, ITotpService totp, ILoginThrottle throttle, IAuditService audit) Build()
    {
        var test = new TestDb();
        var jwt = new JwtService(Options.Create(new JwtOptions
        {
            Issuer = "test", Audience = "test",
            Key = "test-key-with-more-than-thirty-two-characters-please",
            AccessTokenMinutes = 15, RefreshTokenDays = 30,
            EmailVerifyHours = 24, PasswordResetHours = 1
        }));
        var email = new RecordingEmail();
        var totp = new TotpService();
        var throttle = new LoginThrottle();
        var audit = new AuditService(test.Db);
        var sut = new AuthService(test.Db, jwt, email, totp, audit, throttle, Options.Create(new JwtOptions
        {
            Issuer = "test", Audience = "test",
            Key = "test-key-with-more-than-thirty-two-characters-please",
            AccessTokenMinutes = 15, RefreshTokenDays = 30,
            EmailVerifyHours = 24, PasswordResetHours = 1
        }));
        return (sut, test.Db, email, jwt, totp, throttle, audit);
    }

    private static string ClientHashOf(string password) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(password))).ToLowerInvariant();

    [Fact]
    public async Task RegisterAsync_persists_user_and_records_verify_send_timestamp()
    {
        var (sut, db, email, _, _, _, _) = Build();

        var user = await sut.RegisterAsync("alice", "alice@example.com", ClientHashOf("hunter22"));

        var stored = await db.Users.SingleAsync(u => u.Username == "alice");
        stored.Email.Should().Be("alice@example.com");
        stored.EmailVerifySentAt.Should().NotBeNull("registration must record when the verify link was sent");
        stored.EmailVerifiedAt.Should().BeNull("the user has not clicked the link yet");
        email.Verifications.Should().HaveCount(1);
    }

    [Fact]
    public async Task RegisterAsync_rejects_a_raw_password_lookalike_to_avoid_serverside_hashing_of_plaintext()
    {
        var (sut, _, _, _, _, _, _) = Build();

        var act = async () => await sut.RegisterAsync("alice", "a@b.co", "obviously-not-a-sha256");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task LoginAsync_returns_user_on_correct_password_and_null_on_wrong_password()
    {
        var (sut, _, _, _, _, _, _) = Build();
        await sut.RegisterAsync("alice", "alice@example.com", ClientHashOf("hunter22"));

        var ok = await sut.LoginAsync("alice", ClientHashOf("hunter22"));
        var bad = await sut.LoginAsync("alice", ClientHashOf("WRONG"));

        ok.Should().NotBeNull();
        bad.Should().BeNull();
    }

    [Fact]
    public async Task LoginAsync_returns_null_for_an_unknown_username_in_constant_time_and_bumps_throttle()
    {
        // Can't reliably assert wall-clock timing in a unit test, but we can
        // assert the failure-counter behaviour: the throttle should record a
        // failure regardless of whether the user exists.
        var (sut, _, _, _, _, throttle, _) = Build();

        for (int i = 0; i < 5; i++) (await sut.LoginAsync("ghost", ClientHashOf("x"))).Should().BeNull();

        var act = () => throttle.EnsureNotLocked("ghost");
        act.Should().Throw<AuthFailedException>();
    }

    [Fact]
    public async Task BeginLoginAsync_returns_a_TOTP_challenge_when_the_account_has_TOTP_enabled()
    {
        var (sut, db, _, _, totp, _, _) = Build();
        await sut.RegisterAsync("alice", "alice@example.com", ClientHashOf("hunter22"));

        // Mark the account as TOTP-enabled directly so we don't have to
        // round-trip through the enrol flow for this test.
        var u = await db.Users.SingleAsync();
        u.TotpSecret = totp.GenerateSecret();
        u.TotpEnabledAt = DateTime.UtcNow;
        u.EmailVerifiedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var stage = await sut.BeginLoginAsync("alice", ClientHashOf("hunter22"));

        stage.RequiresTotp.Should().BeTrue();
        stage.User.Should().BeNull();
        stage.ChallengeToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ChangePasswordAsync_invalidates_old_password_and_revokes_active_refresh_tokens()
    {
        var (sut, db, _, _, _, _, _) = Build();
        var user = await sut.RegisterAsync("alice", "alice@example.com", ClientHashOf("hunter22"));
        // Mint a refresh token so we can observe revocation.
        await sut.IssueRefreshTokenAsync(user.Id);

        await sut.ChangePasswordAsync(user.Id, ClientHashOf("hunter22"), ClientHashOf("new-pass"));

        (await sut.LoginAsync("alice", ClientHashOf("hunter22"))).Should().BeNull("old password must no longer work");
        (await sut.LoginAsync("alice", ClientHashOf("new-pass"))).Should().NotBeNull();
        (await db.RefreshTokens.AllAsync(t => t.RevokedAt != null)).Should().BeTrue("every active refresh token gets revoked on a password change");
    }

    [Fact]
    public async Task ChangePasswordAsync_rejects_wrong_current_password()
    {
        var (sut, _, _, _, _, _, _) = Build();
        var user = await sut.RegisterAsync("alice", "alice@example.com", ClientHashOf("hunter22"));

        var act = async () => await sut.ChangePasswordAsync(user.Id, ClientHashOf("WRONG"), ClientHashOf("new-pass"));

        await act.Should().ThrowAsync<AuthFailedException>();
    }

    [Fact]
    public async Task ResetPasswordAsync_clears_TOTP_so_a_lost_authenticator_isnt_a_permanent_lockout()
    {
        var (sut, db, _, jwt, totp, _, _) = Build();
        var user = await sut.RegisterAsync("alice", "alice@example.com", ClientHashOf("hunter22"));
        var u = await db.Users.SingleAsync();
        u.TotpSecret = totp.GenerateSecret();
        u.TotpEnabledAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var resetToken = jwt.CreatePasswordResetToken(user.Id);
        await sut.ResetPasswordAsync(resetToken, ClientHashOf("new-pass"));

        var refreshed = await db.Users.SingleAsync();
        refreshed.TotpSecret.Should().BeNull();
        refreshed.TotpEnabledAt.Should().BeNull();
    }

    [Fact]
    public async Task RefreshAsync_rotates_the_token_and_returns_a_new_one()
    {
        var (sut, db, _, _, _, _, _) = Build();
        var user = await sut.RegisterAsync("alice", "alice@example.com", ClientHashOf("hunter22"));
        // Mark verified so the issued tokens look real.
        user.EmailVerifiedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var (raw, _) = await sut.IssueRefreshTokenAsync(user.Id);

        var (tokens, _) = await sut.RefreshAsync(raw);

        tokens.RefreshToken.Should().NotBe(raw, "rotation must hand out a fresh refresh token");
        tokens.AccessToken.Should().NotBeNullOrEmpty();
        // The old token must now be revoked.
        var rotated = await db.RefreshTokens.OrderBy(t => t.CreatedAt).ToListAsync();
        rotated.Should().HaveCount(2, "one revoked + one freshly issued");
        rotated[0].RevokedAt.Should().NotBeNull();
        rotated[1].RevokedAt.Should().BeNull();
    }

    private sealed class RecordingEmail : IEmailService
    {
        public List<string> Verifications { get; } = new();
        public List<string> Resets { get; } = new();
        public List<string> EmailChanges { get; } = new();
        public List<(string To, string Action)> Alerts { get; } = new();

        public Task SendVerificationAsync(string to, string token) { Verifications.Add(to); return Task.CompletedTask; }
        public Task SendPasswordResetAsync(string to, string token) { Resets.Add(to); return Task.CompletedTask; }
        public Task SendEmailChangeAsync(string to, string token) { EmailChanges.Add(to); return Task.CompletedTask; }
        public Task SendSecurityAlertAsync(string to, string label, string? note = null) { Alerts.Add((to, label)); return Task.CompletedTask; }
        public Task SendCommentNotificationAsync(string to, string postTitle, string postSlug, Guid commentId, string commenter, string body) => Task.CompletedTask;
    }
}
