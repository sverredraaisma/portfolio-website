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
                new Post
                {
                    Title = "Live Post", Slug = "live", AuthorId = author.Id, Published = true,
                    CreatedAt = DateTime.UtcNow.AddHours(-1),
                    Blocks = System.Text.Json.JsonDocument.Parse(
                        "{\"blocks\":[{\"type\":\"header\",\"id\":\"h\",\"data\":{\"text\":\"Hello\",\"level\":1}}," +
                        "{\"type\":\"text\",\"id\":\"t\",\"data\":{\"markdown\":\"the\\nfirst\\ntext block body\"}}]}")
                },
                new Post { Title = "Draft", Slug = "draft", AuthorId = author.Id, Published = false }
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

    // ---- Per-tag feed -----------------------------------------------------

    [Theory]
    [InlineData("/rss/UPPER.xml")]                    // mixed case
    [InlineData("/rss/-leading.xml")]                 // bad boundary
    [InlineData("/rss/trailing-.xml")]                // bad boundary
    [InlineData("/rss/has_underscore.xml")]           // disallowed char
    [InlineData("/rss/has space.xml")]                // disallowed char
    [InlineData("/rss/way-too-long-tag-that-exceeds-the-thirty-two-char-cap.xml")]
    public async Task GET_rss_per_tag_returns_404_for_a_malformed_tag(string url)
    {
        // Fail closed on bad input rather than running an arbitrary string
        // through the filter — same shape rule as the in-app NormaliseTags.
        var response = await _client.GetAsync(url);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }

    [Fact(Skip = "EF can't translate Tags.Contains over the SQLite value-converted CSV column. The behaviour is exercised against Postgres in the e2e/manual paths; same skip rationale as PostMethodsTests.List_filters_by_tag.")]
    public Task GET_rss_per_tag_returns_a_valid_empty_feed_when_no_post_carries_the_tag() => Task.CompletedTask;

    [Fact(Skip = "EF can't translate Tags.Contains over the SQLite value-converted CSV column.")]
    public Task GET_rss_per_tag_carries_the_same_short_cache_header_as_the_main_feed() => Task.CompletedTask;

    // ---- Atom feed --------------------------------------------------------

    [Fact]
    public async Task GET_atom_xml_returns_application_atom_xml_with_only_published_posts()
    {
        var response = await _client.GetAsync("/atom.xml");

        response.EnsureSuccessStatusCode();
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/atom+xml");

        var doc = XDocument.Parse(await response.Content.ReadAsStringAsync());
        XNamespace atom = "http://www.w3.org/2005/Atom";
        var entries = doc.Descendants(atom + "entry").ToList();

        entries.Should().ContainSingle(e => e.Element(atom + "title")!.Value == "Live Post");
        entries.Should().NotContain(e => e.Element(atom + "title")!.Value == "Draft",
            "drafts must not appear in the public feed");
    }

    [Fact]
    public async Task GET_atom_xml_uses_a_stable_urn_uuid_id_so_slug_renames_dont_break_subscribers()
    {
        // Atom <id> is a permanent identifier — if it changed when a slug
        // changed, every reader would re-flag the post as new. urn:uuid:
        // form ties it to the immutable Post.Id instead.
        var response = await _client.GetAsync("/atom.xml");
        var doc = XDocument.Parse(await response.Content.ReadAsStringAsync());
        XNamespace atom = "http://www.w3.org/2005/Atom";

        var ids = doc.Descendants(atom + "entry").Select(e => e.Element(atom + "id")!.Value).ToList();
        ids.Should().NotBeEmpty();
        ids.Should().AllSatisfy(id => id.Should().StartWith("urn:uuid:"));
    }

    [Fact]
    public async Task GET_atom_xml_advertises_a_self_link_so_aggregators_can_re_subscribe_after_a_redirect()
    {
        var response = await _client.GetAsync("/atom.xml");
        var doc = XDocument.Parse(await response.Content.ReadAsStringAsync());
        XNamespace atom = "http://www.w3.org/2005/Atom";

        var selfLink = doc.Root!.Elements(atom + "link")
            .FirstOrDefault(l => (string?)l.Attribute("rel") == "self");
        selfLink.Should().NotBeNull();
        selfLink!.Attribute("href")!.Value.Should().EndWith("/atom.xml");
    }

    [Fact]
    public async Task GET_atom_xml_feed_id_equals_the_self_link_so_aggregators_dont_treat_origin_drift_as_a_feed_move()
    {
        // Aggregators use feed <id> to detect "the feed moved" and migrate
        // subscriptions. If we keyed on the alternate HTML page (/posts),
        // a future change to that landing page would force every reader
        // to resubscribe. Keying on the self-URL (/atom.xml) couples the
        // identity to the served-from path, which is what we control.
        var response = await _client.GetAsync("/atom.xml");
        var doc = XDocument.Parse(await response.Content.ReadAsStringAsync());
        XNamespace atom = "http://www.w3.org/2005/Atom";

        var feedId = doc.Root!.Element(atom + "id")!.Value;
        var selfHref = doc.Root!.Elements(atom + "link")
            .First(l => (string?)l.Attribute("rel") == "self")
            .Attribute("href")!.Value;

        feedId.Should().Be(selfHref);
    }

    [Fact]
    public async Task GET_atom_xml_carries_the_same_short_cache_header_as_rss()
    {
        var response = await _client.GetAsync("/atom.xml");

        response.Headers.CacheControl!.MaxAge.Should().Be(TimeSpan.FromHours(1));
    }

    [Fact]
    public async Task GET_rss_xml_includes_an_item_description_pulled_from_the_first_text_block()
    {
        var response = await _client.GetAsync("/rss.xml");
        var doc = XDocument.Parse(await response.Content.ReadAsStringAsync());

        var description = doc.Descendants("item")
            .First(i => i.Element("title")!.Value == "Live Post")
            .Element("description")!.Value;

        // Whitespace-collapsed version of the seeded markdown.
        description.Should().Contain("first text block body");
        description.Should().NotContain("\n", "newlines should be collapsed for the preview");
    }
}

/// Pure-function tests for the description extractor — covered separately
/// so we don't have to spin up an HTTP host for every edge case.
public class RssEndpointDescriptionTests
{
    private static System.Text.Json.JsonDocument Doc(string raw) => System.Text.Json.JsonDocument.Parse(raw);

    [Fact]
    public void Returns_first_text_block_markdown()
    {
        var d = Doc("{\"blocks\":[{\"type\":\"text\",\"data\":{\"markdown\":\"hello world\"}}]}");
        PortfolioApi.Rpc.RssEndpoint.ExtractDescription(d).Should().Be("hello world");
    }

    [Fact]
    public void Falls_back_to_first_header_when_no_text_block_exists()
    {
        var d = Doc("{\"blocks\":[{\"type\":\"header\",\"data\":{\"text\":\"My title\",\"level\":1}}]}");
        PortfolioApi.Rpc.RssEndpoint.ExtractDescription(d).Should().Be("My title");
    }

    [Fact]
    public void Prefers_text_over_header_even_if_the_header_appears_first()
    {
        var d = Doc("{\"blocks\":[" +
                    "{\"type\":\"header\",\"data\":{\"text\":\"Title\",\"level\":1}}," +
                    "{\"type\":\"text\",\"data\":{\"markdown\":\"the body\"}}]}");
        PortfolioApi.Rpc.RssEndpoint.ExtractDescription(d).Should().Be("the body");
    }

    [Fact]
    public void Collapses_runs_of_whitespace_so_the_preview_is_one_line()
    {
        var d = Doc("{\"blocks\":[{\"type\":\"text\",\"data\":{\"markdown\":\"hello\\n\\n   world\"}}]}");
        PortfolioApi.Rpc.RssEndpoint.ExtractDescription(d).Should().Be("hello world");
    }

    [Fact]
    public void Clips_overly_long_markdown_with_an_ellipsis()
    {
        var long_ = new string('a', 400);
        var d = Doc("{\"blocks\":[{\"type\":\"text\",\"data\":{\"markdown\":\"" + long_ + "\"}}]}");
        var result = PortfolioApi.Rpc.RssEndpoint.ExtractDescription(d);
        result.Length.Should().Be(280);
        result.Should().EndWith("…");
    }

    [Fact]
    public void Returns_empty_string_when_there_are_no_blocks_at_all()
    {
        PortfolioApi.Rpc.RssEndpoint.ExtractDescription(Doc("{\"blocks\":[]}")).Should().BeEmpty();
        PortfolioApi.Rpc.RssEndpoint.ExtractDescription(Doc("{}")).Should().BeEmpty();
    }

    [Fact]
    public void Skips_blocks_with_unknown_or_malformed_shapes()
    {
        // image blocks have no markdown/text — skipped without throwing.
        var d = Doc("{\"blocks\":[" +
                    "{\"type\":\"image\",\"data\":{\"src\":\"/x.webp\"}}," +
                    "{\"type\":\"text\",\"data\":{\"markdown\":\"after the image\"}}]}");
        PortfolioApi.Rpc.RssEndpoint.ExtractDescription(d).Should().Be("after the image");
    }
}
