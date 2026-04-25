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
using PortfolioApi.Tests.Infrastructure;

namespace PortfolioApi.Tests.E2e;

[Collection("e2e")]
public class LocationE2eTests : IClassFixture<AppFactory>, IAsyncLifetime
{
    private readonly AppFactory _app;
    private HttpClient _client = null!;
    private string _aliceToken = "";

    public LocationE2eTests(AppFactory app) => _app = app;

    public async Task InitializeAsync()
    {
        await _app.ResetDatabaseAsync();
        _app.Email.Verifications.Clear();
        _client = _app.CreateClient();

        // One verified user for the share flow.
        await Rpc("auth.register", new { username = "alice", email = "a@x", clientHash = ClientHashOf("pw") });
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var u = await db.Users.SingleAsync();
            u.EmailVerifiedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        var login = await Rpc("auth.login", new { username = "alice", clientHash = ClientHashOf("pw") });
        _aliceToken = login.GetProperty("result").GetProperty("tokens").GetProperty("accessToken").GetString()!;
    }

    public Task DisposeAsync() { _client.Dispose(); return Task.CompletedTask; }

    private static string ClientHashOf(string pw) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pw))).ToLowerInvariant();

    private async Task<JsonElement> Rpc(string method, object? @params = null, string? bearer = null)
    {
        var msg = new HttpRequestMessage(HttpMethod.Post, "/rpc")
        { Content = JsonContent.Create(new { method, @params }) };
        if (bearer is not null) msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        var resp = await _client.SendAsync(msg);
        return JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
    }

    private async Task<HttpResponseMessage> RpcStatus(string method, object @params, string? bearer = null)
    {
        var msg = new HttpRequestMessage(HttpMethod.Post, "/rpc")
        { Content = JsonContent.Create(new { method, @params }) };
        if (bearer is not null) msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return await _client.SendAsync(msg);
    }

    [Fact]
    public async Task Anonymous_caller_can_list_an_empty_map()
    {
        var page = await Rpc("location.list");
        page.GetProperty("result").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Sharing_coords_appears_in_the_public_list_with_rounded_precision()
    {
        await Rpc("location.shareCoords",
            new { latitude = 52.3791234, longitude = 4.9001234, label = "home" },
            _aliceToken);

        var list = await Rpc("location.list");
        var items = list.GetProperty("result").EnumerateArray().ToList();

        items.Should().HaveCount(1);
        items[0].GetProperty("username").GetString().Should().Be("alice");
        items[0].GetProperty("latitude").GetDouble().Should().Be(52.379);
        items[0].GetProperty("longitude").GetDouble().Should().Be(4.900);
        items[0].GetProperty("label").GetString().Should().Be("home");
    }

    [Fact]
    public async Task Sharing_requires_authentication()
    {
        var resp = await RpcStatus("location.shareCoords",
            new { latitude = 0.0, longitude = 0.0 }, bearer: null);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Out_of_range_coords_are_rejected_with_400()
    {
        var resp = await RpcStatus("location.shareCoords",
            new { latitude = 999.0, longitude = 0.0 }, _aliceToken);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Clearing_removes_the_user_from_the_public_list()
    {
        await Rpc("location.shareCoords", new { latitude = 1.0, longitude = 1.0 }, _aliceToken);
        await Rpc("location.clear", null, _aliceToken);

        var list = await Rpc("location.list");
        list.GetProperty("result").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task GetMine_requires_auth_and_returns_the_callers_row()
    {
        await Rpc("location.shareCoords", new { latitude = 1.5, longitude = 2.5, label = "spot" }, _aliceToken);

        var anon = await RpcStatus("location.getMine", new { }, bearer: null);
        anon.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var mine = await Rpc("location.getMine", null, _aliceToken);
        mine.GetProperty("result").GetProperty("label").GetString().Should().Be("spot");
    }

    [Fact]
    public async Task Account_export_reflects_what_is_actually_stored_rounded_to_the_chosen_precision()
    {
        // The server rounds inputs to the user's chosen precision tier on
        // store (defence in depth — the frontend also rounds before the
        // RPC call). The export echoes what's stored so the user sees
        // exactly what their account holds: never finer than the tier they
        // picked, even if a misbehaving client sent more decimals.
        await Rpc("location.shareCoords",
            new { latitude = 52.3791234, longitude = 4.9001234, label = "home", precisionDecimals = 3 },
            _aliceToken);

        var export = await Rpc("account.export", null, _aliceToken);
        var loc = export.GetProperty("result").GetProperty("sharedLocation");

        loc.GetProperty("latitude").GetDouble().Should().Be(52.379);
        loc.GetProperty("longitude").GetDouble().Should().Be(4.900);
        loc.GetProperty("precisionDecimals").GetInt32().Should().Be(3);
    }
}
