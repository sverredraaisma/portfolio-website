using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PortfolioApi.Data;
using PortfolioApi.Models;
using PortfolioApi.Tests.Infrastructure;

namespace PortfolioApi.Tests.E2e;

[Collection("e2e")]
public class CommentsFlowE2eTests : IClassFixture<AppFactory>, IAsyncLifetime
{
    private readonly AppFactory _app;
    private HttpClient _client = null!;
    private Guid _postId;
    private string _aliceToken = "";
    private string _bobToken = "";
    private string _adminToken = "";

    public CommentsFlowE2eTests(AppFactory app) => _app = app;

    public async Task InitializeAsync()
    {
        await _app.ResetDatabaseAsync();
        _app.Email.Verifications.Clear();
        _app.Email.Alerts.Clear();
        _client = _app.CreateClient();

        // Seed: alice, bob (both verified members), one admin "owner" who
        // owns one published post. Issue access tokens by going through the
        // full register → verify → login flow for each — exercises real
        // wire-level auth so the comment tests aren't faking authn state.
        _aliceToken = await RegisterAndLogin("alice", "a@x", "pwa");
        _bobToken   = await RegisterAndLogin("bob",   "b@x", "pwb");

        // Promote a third user to admin and seed the post.
        Guid adminId;
        await Rpc("auth.register", new { username = "owner", email = "o@x", clientHash = ClientHashOf("pwo") });
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var owner = await db.Users.SingleAsync(u => u.Username == "owner");
            owner.IsAdmin = true;
            owner.EmailVerifiedAt = DateTime.UtcNow;
            adminId = owner.Id;

            var post = new Post { Title = "p", Slug = "p", AuthorId = adminId, Published = true };
            db.Posts.Add(post);
            await db.SaveChangesAsync();
            _postId = post.Id;
        }
        var loginAdmin = await Rpc("auth.login", new { username = "owner", clientHash = ClientHashOf("pwo") });
        _adminToken = loginAdmin.GetProperty("result").GetProperty("tokens").GetProperty("accessToken").GetString()!;
    }

    public Task DisposeAsync() { _client.Dispose(); return Task.CompletedTask; }

    private static string ClientHashOf(string pw) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pw))).ToLowerInvariant();

    private async Task<string> RegisterAndLogin(string username, string email, string pw)
    {
        await Rpc("auth.register", new { username, email, clientHash = ClientHashOf(pw) });
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var u = await db.Users.SingleAsync(x => x.Username == username);
            u.EmailVerifiedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        var login = await Rpc("auth.login", new { username, clientHash = ClientHashOf(pw) });
        return login.GetProperty("result").GetProperty("tokens").GetProperty("accessToken").GetString()!;
    }

    private async Task<JsonElement> Rpc(string method, object? @params = null, string? bearer = null)
    {
        var msg = new HttpRequestMessage(HttpMethod.Post, "/rpc")
        { Content = JsonContent.Create(new { method, @params }) };
        if (bearer is not null) msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        var resp = await _client.SendAsync(msg);
        var raw = await resp.Content.ReadAsStringAsync();
        return JsonDocument.Parse(raw).RootElement;
    }

    private async Task<HttpResponseMessage> RpcStatus(string method, object @params, string? bearer = null)
    {
        var msg = new HttpRequestMessage(HttpMethod.Post, "/rpc")
        { Content = JsonContent.Create(new { method, @params }) };
        if (bearer is not null) msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return await _client.SendAsync(msg);
    }

    [Fact]
    public async Task Anonymous_caller_can_list_an_empty_thread()
    {
        var page = await Rpc("comments.list", new { postId = _postId });
        page.GetProperty("result").GetProperty("items").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Create_then_list_round_trips_with_the_callers_username()
    {
        await Rpc("comments.create", new { postId = _postId, body = "first!" }, _aliceToken);

        var page = await Rpc("comments.list", new { postId = _postId });
        var items = page.GetProperty("result").GetProperty("items").EnumerateArray().ToList();

        items.Should().HaveCount(1);
        items[0].GetProperty("body").GetString().Should().Be("first!");
        items[0].GetProperty("author").GetString().Should().Be("alice");
        items[0].GetProperty("authorIsAdmin").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Create_requires_authentication()
    {
        var resp = await RpcStatus("comments.create",
            new { postId = _postId, body = "ghost" }, bearer: null);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Create_rejects_an_empty_body_with_400_invalid()
    {
        var resp = await RpcStatus("comments.create",
            new { postId = _postId, body = "   " }, _aliceToken);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Author_can_edit_their_own_comment_via_comments_update()
    {
        var created = await Rpc("comments.create",
            new { postId = _postId, body = "v1" }, _aliceToken);
        var id = created.GetProperty("result").GetProperty("id").GetString();

        var updated = await Rpc("comments.update",
            new { id, body = "v2" }, _aliceToken);

        updated.GetProperty("result").GetProperty("body").GetString().Should().Be("v2");
    }

    [Fact]
    public async Task Other_users_cannot_edit_someones_else_comment_even_if_admin()
    {
        var created = await Rpc("comments.create",
            new { postId = _postId, body = "alice's words" }, _aliceToken);
        var id = created.GetProperty("result").GetProperty("id").GetString();

        var byBob   = await RpcStatus("comments.update", new { id, body = "tampered" }, _bobToken);
        var byAdmin = await RpcStatus("comments.update", new { id, body = "tampered" }, _adminToken);

        byBob.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        byAdmin.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "admins moderate by deleting; they don't rewrite other users' words");
    }

    [Fact]
    public async Task Author_can_delete_own_and_admin_can_delete_anyones_comment()
    {
        var aliceComment = await Rpc("comments.create", new { postId = _postId, body = "alice" }, _aliceToken);
        var bobComment   = await Rpc("comments.create", new { postId = _postId, body = "bob"   }, _bobToken);
        var aliceId = aliceComment.GetProperty("result").GetProperty("id").GetString();
        var bobId   = bobComment.GetProperty("result").GetProperty("id").GetString();

        // Alice deletes her own.
        var ownDelete = await RpcStatus("comments.delete", new { id = aliceId }, _aliceToken);
        ownDelete.IsSuccessStatusCode.Should().BeTrue();

        // Admin deletes bob's (moderation).
        var modDelete = await RpcStatus("comments.delete", new { id = bobId }, _adminToken);
        modDelete.IsSuccessStatusCode.Should().BeTrue();

        // Both gone.
        var page = await Rpc("comments.list", new { postId = _postId });
        page.GetProperty("result").GetProperty("items").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Non_admin_cannot_delete_someones_else_comment()
    {
        var created = await Rpc("comments.create", new { postId = _postId, body = "alice's" }, _aliceToken);
        var id = created.GetProperty("result").GetProperty("id").GetString();

        var resp = await RpcStatus("comments.delete", new { id }, _bobToken);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListAll_admin_only_returns_cross_post_rows()
    {
        await Rpc("comments.create", new { postId = _postId, body = "x" }, _aliceToken);

        var anon  = await RpcStatus("comments.listAll", new { }, bearer: null);
        var member = await RpcStatus("comments.listAll", new { }, _aliceToken);
        var admin  = await Rpc("comments.listAll", new { }, _adminToken);

        anon.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        member.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        admin.GetProperty("result").GetProperty("items").GetArrayLength().Should().Be(1);
        admin.GetProperty("result").GetProperty("items")[0].GetProperty("postSlug").GetString().Should().Be("p");
    }
}
