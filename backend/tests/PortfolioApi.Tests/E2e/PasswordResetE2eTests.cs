using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PortfolioApi.Data;
using PortfolioApi.Tests.Infrastructure;

namespace PortfolioApi.Tests.E2e;

/// Drives the full forgot → reset round-trip over /rpc. Captures the JWT
/// out of the recording email service so the test can submit it back to
/// auth.resetPassword.
[Collection("e2e")]
public class PasswordResetE2eTests : IClassFixture<AppFactory>, IAsyncLifetime
{
    private readonly AppFactory _app;
    private HttpClient _client = null!;

    public PasswordResetE2eTests(AppFactory app) => _app = app;

    public async Task InitializeAsync()
    {
        await _app.ResetDatabaseAsync();
        _app.Email.Verifications.Clear();
        _app.Email.Resets.Clear();
        _app.Email.EmailChanges.Clear();
        _app.Email.Alerts.Clear();
        _client = _app.CreateClient();
    }

    public Task DisposeAsync() { _client.Dispose(); return Task.CompletedTask; }

    private static string ClientHashOf(string password) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password))).ToLowerInvariant();

    private async Task<JsonElement> Rpc(string method, object @params)
    {
        var resp = await _client.PostAsync("/rpc", JsonContent.Create(new { method, @params }));
        var raw = await resp.Content.ReadAsStringAsync();
        return JsonDocument.Parse(raw).RootElement;
    }

    [Fact]
    public async Task Full_round_trip_register_request_reset_then_login_with_new_password()
    {
        // 1. Register and mark verified so login works at all.
        await Rpc("auth.register", new { username = "alice", email = "alice@example.com", clientHash = ClientHashOf("oldpass") });
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var u = await db.Users.SingleAsync();
            u.EmailVerifiedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        // 2. Request a reset. The recording email gets the JWT.
        await Rpc("auth.requestPasswordReset", new { email = "alice@example.com" });
        _app.Email.Resets.Should().HaveCount(1);
        var (toEmail, token) = _app.Email.Resets[0];
        toEmail.Should().Be("alice@example.com");
        token.Should().NotBeNullOrEmpty();

        // 3. Submit the token + a new client-hash.
        var reset = await Rpc("auth.resetPassword", new { token, clientHash = ClientHashOf("newpass") });
        reset.GetProperty("result").GetRawText().Should().Contain("ok");

        // 4. Old password must no longer work.
        var oldLogin = await _client.PostAsync("/rpc", JsonContent.Create(new
        {
            method = "auth.login",
            @params = new { username = "alice", clientHash = ClientHashOf("oldpass") }
        }));
        oldLogin.IsSuccessStatusCode.Should().BeFalse(
            "the old password must no longer authenticate");

        // 5. New password works.
        var newLogin = await Rpc("auth.login", new { username = "alice", clientHash = ClientHashOf("newpass") });
        newLogin.GetProperty("result").GetProperty("tokens").GetProperty("accessToken").GetString()
            .Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Reset_request_for_unknown_email_does_not_leak_account_existence()
    {
        // No account at this address — the response shape must still look
        // like success so an attacker can't enumerate registered emails.
        var resp = await _client.PostAsync("/rpc", JsonContent.Create(new
        {
            method = "auth.requestPasswordReset",
            @params = new { email = "ghost@example.com" }
        }));

        resp.IsSuccessStatusCode.Should().BeTrue();
        _app.Email.Resets.Should().BeEmpty("no email should be sent for an unknown address");
    }

    [Fact]
    public async Task Reset_with_a_garbage_token_returns_401_unauthorized()
    {
        var resp = await _client.PostAsync("/rpc", JsonContent.Create(new
        {
            method = "auth.resetPassword",
            @params = new { token = "not.a.real.jwt", clientHash = ClientHashOf("anything") }
        }));

        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }
}
