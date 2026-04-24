using System.Xml.Linq;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PortfolioApi.Data;
using PortfolioApi.Models;
using PortfolioApi.Rpc;

/// In-process host wires only the bits RssEndpoint needs (an AppDbContext +
/// the endpoint mapping) so we can hit /rss.xml with a real HttpClient and
/// inspect the XML.
namespace PortfolioApi.Tests.Rpc;

public class RssEndpointTests : IAsyncLifetime
{
    private SqliteConnection _conn = null!;
    private IHost _host = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        // One long-lived in-memory connection shared across the whole test —
        // ":memory:" databases vanish when the connection closes, so a per-
        // request scope would see an empty DB.
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();

        var builder = Host.CreateDefaultBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(s =>
                {
                    s.AddDbContext<AppDbContext>(o => o.UseSqlite(_conn));
                    s.AddRouting();
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(e => e.MapRss());
                });
            });
        _host = await builder.StartAsync();

        using (var scope = _host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
            var author = new User { Username = "alice", Email = "a@x", PasswordHash = new byte[]{1}, PasswordSalt = new byte[]{1} };
            db.Users.Add(author);
            db.Posts.AddRange(
                new Post { Title = "Live Post", Slug = "live", AuthorId = author.Id, Published = true,  CreatedAt = DateTime.UtcNow.AddHours(-1) },
                new Post { Title = "Draft",     Slug = "draft", AuthorId = author.Id, Published = false }
            );
            await db.SaveChangesAsync();
        }

        _client = _host.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
        _conn.Dispose();
    }

    [Fact]
    public async Task GET_rss_xml_returns_application_rss_xml_with_only_published_posts()
    {
        var response = await _client.GetAsync("/rss.xml");

        response.EnsureSuccessStatusCode();
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/rss+xml");

        var doc = XDocument.Parse(await response.Content.ReadAsStringAsync());
        var titles = doc.Descendants("item").Select(i => i.Element("title")!.Value).ToList();

        titles.Should().ContainSingle(t => t == "Live Post");
        titles.Should().NotContain("Draft", "drafts must not appear in the public feed");
    }

    [Fact]
    public async Task GET_rss_xml_includes_a_channel_with_a_link_pointing_back_at_the_origin()
    {
        var response = await _client.GetAsync("/rss.xml");
        var doc = XDocument.Parse(await response.Content.ReadAsStringAsync());

        var channel = doc.Root!.Element("channel")!;
        channel.Element("title").Should().NotBeNull();
        channel.Element("link")!.Value.Should().StartWith("http").And.EndWith("/posts");
    }

    [Fact]
    public async Task GET_rss_xml_sets_a_short_cache_header_so_feed_readers_dont_hammer_the_DB()
    {
        var response = await _client.GetAsync("/rss.xml");

        response.Headers.CacheControl!.MaxAge.Should().Be(TimeSpan.FromHours(1));
    }
}
