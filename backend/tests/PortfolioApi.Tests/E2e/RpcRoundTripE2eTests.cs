using System.Net;
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

/// Drives the full /rpc pipeline (router + auth + Argon2 hashing + DB writes
/// + security headers) over a real HttpClient. Each test starts from a
/// freshly-created DB so order doesn't matter.
public class RpcRoundTripE2eTests : IClassFixture<AppFactory>, IAsyncLifetime
{
    private readonly AppFactory _app;
    private HttpClient _client = null!;

    public RpcRoundTripE2eTests(AppFactory app) => _app = app;

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

    private async Task<JsonElement> Rpc(string method, object? @params = null)
    {
        var body = JsonContent.Create(new { method, @params });
        var resp = await _client.PostAsync("/rpc", body);
        var raw = await resp.Content.ReadAsStringAsync();
        try
        {
            return JsonDocument.Parse(raw).RootElement;
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(
                $"RPC '{method}' returned non-JSON {(int)resp.StatusCode} body: {raw[..Math.Min(raw.Length, 500)]}");
        }
    }

    [Fact]
    public async Task Unknown_method_returns_404_with_an_error_envelope()
    {
        var body = await Rpc("does.not.exist");

        body.GetProperty("error").GetProperty("code").GetString().Should().Be("not_found");
    }

    [Fact]
    public async Task Register_then_login_round_trips_to_a_session()
    {
        // 1. register
        var register = await Rpc("auth.register", new { username = "alice", email = "alice@example.com", clientHash = ClientHashOf("hunter22") });
        register.GetProperty("result").GetProperty("username").GetString().Should().Be("alice");
        _app.Email.Verifications.Should().HaveCount(1, "registration triggers a verify mail");

        // 2. simulate the user clicking the verify link by flipping the
        //    EmailVerifiedAt directly. The full email-verify flow is covered
        //    in AuthService unit tests; here we just want a usable session.
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var u = await db.Users.SingleAsync();
            u.EmailVerifiedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        // 3. login
        var login = await Rpc("auth.login", new { username = "alice", clientHash = ClientHashOf("hunter22") });
        var tokens = login.GetProperty("result").GetProperty("tokens");
        tokens.GetProperty("accessToken").GetString().Should().NotBeNullOrEmpty();
        tokens.GetProperty("refreshToken").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Login_with_wrong_password_returns_401_unauthorized()
    {
        // Set up a verified account first.
        await Rpc("auth.register", new { username = "alice", email = "a@x", clientHash = ClientHashOf("hunter22") });
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var u = await db.Users.SingleAsync();
            u.EmailVerifiedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var resp = await _client.PostAsync("/rpc", JsonContent.Create(new
        {
            method = "auth.login",
            @params = new { username = "alice", clientHash = ClientHashOf("WRONG") }
        }));

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        body.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("unauthorized");
    }

    [Fact]
    public async Task Anonymous_caller_can_list_published_posts_but_create_is_blocked()
    {
        // Bypass the admin gate to seed a published post directly.
        Guid authorId;
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var admin = new PortfolioApi.Models.User
            {
                Username = "owner", Email = "o@x", IsAdmin = true,
                PasswordHash = new byte[]{1}, PasswordSalt = new byte[]{1},
                EmailVerifiedAt = DateTime.UtcNow
            };
            db.Users.Add(admin);
            db.Posts.Add(new PortfolioApi.Models.Post
            {
                Title = "Hello", Slug = "hello", AuthorId = admin.Id, Published = true
            });
            await db.SaveChangesAsync();
            authorId = admin.Id;
        }

        var list = await Rpc("posts.list");
        // Fail fast with the raw body if the call errored — cuts a layer of
        // indirection from "Null GetProperty" exceptions during debugging.
        if (!list.TryGetProperty("result", out var result) || result.ValueKind == JsonValueKind.Null)
            throw new InvalidOperationException("posts.list returned non-result envelope: " + list.GetRawText());
        var items = result.GetProperty("items").EnumerateArray().ToList();
        items.Should().HaveCount(1);
        items[0].GetProperty("slug").GetString().Should().Be("hello");

        // Create from anonymous → 401
        var resp = await _client.PostAsync("/rpc", JsonContent.Create(new
        {
            method = "posts.create",
            @params = new { title = "x", slug = "x", blocks = new { blocks = Array.Empty<object>() } }
        }));
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Security_headers_are_present_on_responses()
    {
        var resp = await _client.PostAsync("/rpc", JsonContent.Create(new { method = "posts.list" }));

        resp.Headers.TryGetValues("X-Content-Type-Options", out var nosniff).Should().BeTrue();
        nosniff!.Should().ContainSingle().Which.Should().Be("nosniff");

        resp.Headers.TryGetValues("X-Frame-Options", out var frame).Should().BeTrue();
        frame!.Should().ContainSingle().Which.Should().Be("DENY");

        resp.Headers.TryGetValues("Referrer-Policy", out var ref_).Should().BeTrue();
        ref_!.Should().ContainSingle().Which.Should().Be("no-referrer");

        resp.Headers.TryGetValues("Cross-Origin-Opener-Policy", out var coop).Should().BeTrue();
        coop!.Should().ContainSingle().Which.Should().Be("same-origin");
    }

    [Fact]
    public async Task Content_Security_Policy_locks_down_frames_objects_base_and_form_targets()
    {
        var resp = await _client.PostAsync("/rpc", JsonContent.Create(new { method = "posts.list" }));

        resp.Headers.TryGetValues("Content-Security-Policy", out var values).Should().BeTrue();
        var csp = values!.Single();

        // The *protective* directives — these are the ones that meaningfully
        // close attack vectors regardless of the unsafe-inline tradeoff.
        csp.Should().Contain("frame-ancestors 'none'", "no clickjacking");
        csp.Should().Contain("object-src 'none'",     "no <object>/<embed> exploits");
        csp.Should().Contain("base-uri 'self'",       "base-tag URL hijacking blocked");
        csp.Should().Contain("form-action 'self'",    "form POST exfiltration blocked");
        csp.Should().Contain("default-src 'self'");
    }

    [Fact]
    public async Task Bad_JSON_body_returns_400_bad_request()
    {
        var resp = await _client.PostAsync("/rpc",
            new StringContent("{not json", Encoding.UTF8, "application/json"));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RPC_response_body_is_application_json()
    {
        var resp = await _client.PostAsync("/rpc", JsonContent.Create(new { method = "posts.list" }));

        resp.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }
}
