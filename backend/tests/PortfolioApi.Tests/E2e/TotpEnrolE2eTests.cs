using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PortfolioApi.Data;
using PortfolioApi.Services;
using PortfolioApi.Tests.Infrastructure;

namespace PortfolioApi.Tests.E2e;

/// Drives the full TOTP enrolment flow: login → totpStart → totpConfirm
/// (returns recovery codes) → logout → login again, this time landing on
/// the TOTP challenge → completeTotp → session.
[Collection("e2e")]
public class TotpEnrolE2eTests : IClassFixture<AppFactory>, IAsyncLifetime
{
    private readonly AppFactory _app;
    private HttpClient _client = null!;

    public TotpEnrolE2eTests(AppFactory app) => _app = app;

    public async Task InitializeAsync()
    {
        await _app.ResetDatabaseAsync();
        _app.Email.Verifications.Clear();
        _app.Email.Alerts.Clear();
        _client = _app.CreateClient();
    }

    public Task DisposeAsync() { _client.Dispose(); return Task.CompletedTask; }

    private static string ClientHashOf(string password) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password))).ToLowerInvariant();

    private async Task<JsonElement> Rpc(string method, object? @params, string? bearer = null)
    {
        var msg = new HttpRequestMessage(HttpMethod.Post, "/rpc")
        {
            Content = JsonContent.Create(new { method, @params })
        };
        if (bearer is not null) msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        var resp = await _client.SendAsync(msg);
        var raw = await resp.Content.ReadAsStringAsync();
        return JsonDocument.Parse(raw).RootElement;
    }

    [Fact]
    public async Task Enrol_then_login_requires_a_valid_TOTP_code()
    {
        // 1. register + verify
        await Rpc("auth.register", new { username = "alice", email = "a@x", clientHash = ClientHashOf("pw") }, null);
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var u = await db.Users.SingleAsync();
            u.EmailVerifiedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        // 2. login (no TOTP yet)
        var login = await Rpc("auth.login", new { username = "alice", clientHash = ClientHashOf("pw") }, null);
        var access = login.GetProperty("result").GetProperty("tokens").GetProperty("accessToken").GetString()!;

        // 3. start enrolment as the signed-in user
        var start = await Rpc("auth.totpStart", null, access);
        var secretBase32 = start.GetProperty("result").GetProperty("secretBase32").GetString()!;
        secretBase32.Should().NotBeNullOrEmpty();

        // 4. compute the current code from the base32 secret and confirm
        var code = TotpFor(secretBase32);
        var confirm = await Rpc("auth.totpConfirm", new { code }, access);
        var recovery = confirm.GetProperty("result").GetProperty("recoveryCodes").EnumerateArray().Select(e => e.GetString()!).ToList();
        recovery.Should().HaveCount(10, "an initial sheet of recovery codes is issued on confirmation");

        // 5. log in again — server must hand back a TOTP challenge instead
        //    of session tokens.
        var loginAgain = await Rpc("auth.login", new { username = "alice", clientHash = ClientHashOf("pw") }, null);
        var challenge = loginAgain.GetProperty("result").GetProperty("challenge").GetString();
        challenge.Should().NotBeNullOrEmpty();
        loginAgain.GetProperty("result").TryGetProperty("tokens", out var t).Should().BeTrue();
        t.ValueKind.Should().Be(JsonValueKind.Null);

        // 6. complete the TOTP step → tokens land
        var second = TotpFor(secretBase32);
        var completed = await Rpc("auth.completeTotp", new { challenge, code = second }, null);
        completed.GetProperty("result").GetProperty("tokens").GetProperty("accessToken").GetString()
            .Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Recovery_code_substitutes_for_a_TOTP_code_at_login()
    {
        // Set up an account with TOTP already enrolled.
        await Rpc("auth.register", new { username = "alice", email = "a@x", clientHash = ClientHashOf("pw") }, null);
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var u = await db.Users.SingleAsync();
            u.EmailVerifiedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        var login = await Rpc("auth.login", new { username = "alice", clientHash = ClientHashOf("pw") }, null);
        var access = login.GetProperty("result").GetProperty("tokens").GetProperty("accessToken").GetString()!;
        var start = await Rpc("auth.totpStart", null, access);
        var secret = start.GetProperty("result").GetProperty("secretBase32").GetString()!;
        var confirm = await Rpc("auth.totpConfirm", new { code = TotpFor(secret) }, access);
        var recoveryCodes = confirm.GetProperty("result").GetProperty("recoveryCodes")
            .EnumerateArray().Select(e => e.GetString()!).ToList();

        // Now log in and use a recovery code instead of a TOTP code.
        var loginAgain = await Rpc("auth.login", new { username = "alice", clientHash = ClientHashOf("pw") }, null);
        var challenge = loginAgain.GetProperty("result").GetProperty("challenge").GetString()!;
        var oneCode = recoveryCodes[0];
        var completed = await Rpc("auth.completeTotp", new { challenge, code = oneCode }, null);

        completed.GetProperty("result").GetProperty("tokens").GetProperty("accessToken").GetString()
            .Should().NotBeNullOrEmpty("a recovery code is a valid second factor");

        // Same recovery code can't be reused — single-use.
        var loginThird = await Rpc("auth.login", new { username = "alice", clientHash = ClientHashOf("pw") }, null);
        var challenge2 = loginThird.GetProperty("result").GetProperty("challenge").GetString()!;
        var resp = await _client.PostAsync("/rpc", JsonContent.Create(new
        {
            method = "auth.completeTotp",
            @params = new { challenge = challenge2, code = oneCode }
        }));
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized,
            "recovery codes are single-use");
    }

    // Inline TOTP code generator that mirrors RFC 6238 / 4226 §5.3, so the
    // test is independent of TotpService internals (the service is unit-
    // tested separately).
    private static string TotpFor(string base32Secret)
    {
        var secret = DecodeBase32(base32Secret);
        var counter = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        var counterBytes = new byte[8];
        for (int i = 7; i >= 0; i--) { counterBytes[i] = (byte)(counter & 0xFF); counter >>= 8; }
        using var hmac = new HMACSHA1(secret);
        var hash = hmac.ComputeHash(counterBytes);
        var offset = hash[^1] & 0x0F;
        var binary =
              ((hash[offset]     & 0x7F) << 24)
            | ((hash[offset + 1] & 0xFF) << 16)
            | ((hash[offset + 2] & 0xFF) << 8)
            |  (hash[offset + 3] & 0xFF);
        return (binary % 1_000_000).ToString("D6");
    }

    private static byte[] DecodeBase32(string s)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>((s.Length * 5 + 7) / 8);
        int buffer = 0, bits = 0;
        foreach (var c in s.ToUpperInvariant())
        {
            var v = alphabet.IndexOf(c);
            if (v < 0) continue;
            buffer = (buffer << 5) | v;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                bytes.Add((byte)((buffer >> bits) & 0xFF));
            }
        }
        return bytes.ToArray();
    }
}
