using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PortfolioApi.Configuration;
using PortfolioApi.Data;
using PortfolioApi.Models;
using PortfolioApi.Rpc.Methods;
using PortfolioApi.Services;
using PortfolioApi.Tests.Infrastructure;

namespace PortfolioApi.Tests.Rpc;

public class AuthMethodsTests
{
    private static (AuthMethods sut, AppDbContext db, AuthService auth) Setup()
    {
        var test = new TestDb();
        var jwtOpt = Options.Create(new JwtOptions
        {
            Issuer = "test", Audience = "test",
            Key = "test-key-with-more-than-thirty-two-characters-please",
            AccessTokenMinutes = 15, RefreshTokenDays = 30,
            EmailVerifyHours = 24, PasswordResetHours = 1
        });
        var jwt = new JwtService(jwtOpt);
        var email = new NoopEmail();
        var totp = new TotpService();
        var throttle = new LoginThrottle();
        var audit = new AuditService(test.Db);
        var auth = new AuthService(test.Db, jwt, email, totp, audit, throttle, jwtOpt);
        var passkeys = new NoopPasskeys();
        return (new AuthMethods(auth, jwt, passkeys, test.Db), test.Db, auth);
    }

    private static string ClientHashOf(string password) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(password))).ToLowerInvariant();

    [Fact]
    public async Task Register_returns_a_RegisterResult_with_emailVerified_false()
    {
        var (sut, _, _) = Setup();

        var res = await sut.Register(new RegisterParams
        {
            Username = "alice", Email = "a@x", ClientHash = ClientHashOf("hunter22")
        }, TestRpcContext.Anonymous());

        res.Username.Should().Be("alice");
        res.EmailVerified.Should().BeFalse(
            "registration always reports unverified — verification happens via the email link");
    }

    [Fact]
    public async Task Login_returns_tokens_when_creds_are_correct_and_email_is_verified()
    {
        var (sut, db, _) = Setup();
        await sut.Register(new RegisterParams { Username = "alice", Email = "a@x", ClientHash = ClientHashOf("hunter22") }, TestRpcContext.Anonymous());
        // Pretend the user clicked the verify link.
        var u = await db.Users.SingleAsync();
        u.EmailVerifiedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var res = await sut.Login(new LoginParams
        {
            Username = "alice", ClientHash = ClientHashOf("hunter22")
        }, TestRpcContext.Anonymous());

        res.Tokens.Should().NotBeNull();
        res.Challenge.Should().BeNull();
        res.RequiresTotp.Should().BeFalse();
        res.Tokens!.User.Username.Should().Be("alice");
    }

    [Fact]
    public async Task Login_returns_a_TOTP_challenge_when_2FA_is_enabled()
    {
        var (sut, db, _) = Setup();
        await sut.Register(new RegisterParams { Username = "alice", Email = "a@x", ClientHash = ClientHashOf("hunter22") }, TestRpcContext.Anonymous());
        var u = await db.Users.SingleAsync();
        u.EmailVerifiedAt = DateTime.UtcNow;
        u.TotpSecret = new TotpService().GenerateSecret();
        u.TotpEnabledAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var res = await sut.Login(new LoginParams
        {
            Username = "alice", ClientHash = ClientHashOf("hunter22")
        }, TestRpcContext.Anonymous());

        res.Tokens.Should().BeNull();
        res.Challenge.Should().NotBeNullOrEmpty();
        res.RequiresTotp.Should().BeTrue();
    }

    [Fact]
    public async Task Login_throws_AuthFailedException_on_wrong_password()
    {
        var (sut, db, _) = Setup();
        await sut.Register(new RegisterParams { Username = "alice", Email = "a@x", ClientHash = ClientHashOf("hunter22") }, TestRpcContext.Anonymous());
        var u = await db.Users.SingleAsync();
        u.EmailVerifiedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var act = async () => await sut.Login(new LoginParams
        {
            Username = "alice", ClientHash = ClientHashOf("WRONG")
        }, TestRpcContext.Anonymous());

        await act.Should().ThrowAsync<AuthFailedException>();
    }

    [Fact]
    public async Task Login_throws_InvalidOperation_email_not_verified_when_password_is_right_but_email_not_confirmed()
    {
        var (sut, _, _) = Setup();
        await sut.Register(new RegisterParams { Username = "alice", Email = "a@x", ClientHash = ClientHashOf("hunter22") }, TestRpcContext.Anonymous());

        var act = async () => await sut.Login(new LoginParams
        {
            Username = "alice", ClientHash = ClientHashOf("hunter22")
        }, TestRpcContext.Anonymous());

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Email not verified*");
    }

    [Fact]
    public async Task Me_requires_a_signed_in_user()
    {
        var (sut, _, _) = Setup();

        var act = async () => await sut.Me(TestRpcContext.Anonymous());

        await act.Should().ThrowAsync<AuthFailedException>();
    }

    [Fact]
    public async Task Me_returns_the_signed_in_users_dto()
    {
        var (sut, db, _) = Setup();
        await sut.Register(new RegisterParams { Username = "alice", Email = "a@x", ClientHash = ClientHashOf("hunter22") }, TestRpcContext.Anonymous());
        var u = await db.Users.SingleAsync();

        var dto = await sut.Me(TestRpcContext.User(u.Id));

        dto.Username.Should().Be("alice");
        dto.Email.Should().Be("a@x");
    }

    private sealed class NoopEmail : IEmailService
    {
        public Task SendVerificationAsync(string toEmail, string jwtToken) => Task.CompletedTask;
        public Task SendPasswordResetAsync(string toEmail, string jwtToken) => Task.CompletedTask;
        public Task SendEmailChangeAsync(string toEmail, string jwtToken) => Task.CompletedTask;
        public Task SendSecurityAlertAsync(string toEmail, string actionLabel, string? extraNote = null) => Task.CompletedTask;
        public Task SendCommentNotificationAsync(string toEmail, string postTitle, string postSlug, Guid commentId, string commenter, string body) => Task.CompletedTask;
    }

    private sealed class NoopPasskeys : IPasskeyService
    {
        public Task<PasskeyRegistrationStart> StartRegistrationAsync(User user, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PasskeyDto> FinishRegistrationAsync(Guid userId, string sessionId, string attestationJson, string name, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PasskeyAssertionStart> StartAssertionAsync(string? username, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<User> FinishAssertionAsync(string sessionId, string assertionJson, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<PasskeyDto>> ListAsync(Guid userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteAsync(Guid userId, Guid passkeyId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task RenameAsync(Guid userId, Guid passkeyId, string name, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
