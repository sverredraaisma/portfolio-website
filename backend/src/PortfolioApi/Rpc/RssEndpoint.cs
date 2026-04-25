using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using PortfolioApi.Data;

namespace PortfolioApi.Rpc;

/// RSS 2.0 feed, sitemap, and robots.txt for published posts. All three live
/// outside the RPC router because crawlers and feed readers expect plain GETs
/// against well-known URLs. The feed and sitemap are bounded to the most
/// recent 200 / 50 posts so the bodies never get unwieldy.
public static class RssEndpoint
{
    private const int MaxItems = 50;
    private const int MaxSitemapItems = 1000;

    public static IEndpointRouteBuilder MapRss(this IEndpointRouteBuilder app)
    {
        app.MapGet("/rss.xml", async (HttpContext http, AppDbContext db) =>
        {
            // Reconstruct the public origin from the proxy headers so the feed
            // links work whether the site is reachable on http://localhost or
            // https://example.com.
            var origin = http.Request.Scheme + "://" + http.Request.Host;

            var posts = await db.Posts
                .AsNoTracking()
                .Where(p => p.Published)
                .OrderByDescending(p => p.CreatedAt)
                .Take(MaxItems)
                .Select(p => new
                {
                    p.Title,
                    p.Slug,
                    p.CreatedAt,
                    Author = p.Author!.Username,
                    p.Blocks
                })
                .ToListAsync(http.RequestAborted);

            var xml = new StringBuilder();
            using (var writer = XmlWriter.Create(xml, new XmlWriterSettings
                   {
                       Indent = false,
                       Encoding = new UTF8Encoding(false),
                       OmitXmlDeclaration = false,
                       Async = false
                   }))
            {
                writer.WriteStartDocument();
                writer.WriteStartElement("rss");
                writer.WriteAttributeString("version", "2.0");
                writer.WriteStartElement("channel");

                writer.WriteElementString("title", "sverre.dev");
                writer.WriteElementString("link", origin + "/posts");
                writer.WriteElementString("description", "Posts from sverre.dev");
                writer.WriteElementString("language", "en");
                writer.WriteElementString("lastBuildDate", DateTime.UtcNow.ToString("R", CultureInfo.InvariantCulture));

                foreach (var p in posts)
                {
                    writer.WriteStartElement("item");
                    writer.WriteElementString("title", p.Title);
                    var url = $"{origin}/posts/{p.Slug}";
                    writer.WriteElementString("link", url);
                    writer.WriteElementString("guid", url);
                    writer.WriteElementString("pubDate", p.CreatedAt.ToString("R", CultureInfo.InvariantCulture));
                    writer.WriteElementString("author", p.Author);
                    var description = ExtractDescription(p.Blocks);
                    if (!string.IsNullOrEmpty(description))
                        writer.WriteElementString("description", description);
                    writer.WriteEndElement(); // item
                }

                writer.WriteEndElement(); // channel
                writer.WriteEndElement(); // rss
                writer.WriteEndDocument();
            }

            http.Response.ContentType = "application/rss+xml; charset=utf-8";
            // Short cache so feed readers get new posts within an hour without
            // hammering the database.
            http.Response.Headers["Cache-Control"] = "public, max-age=3600";
            await http.Response.WriteAsync(xml.ToString(), http.RequestAborted);
        });

        app.MapGet("/sitemap.xml", async (HttpContext http, AppDbContext db) =>
        {
            var origin = http.Request.Scheme + "://" + http.Request.Host;

            var posts = await db.Posts
                .AsNoTracking()
                .Where(p => p.Published)
                .OrderByDescending(p => p.UpdatedAt)
                .Take(MaxSitemapItems)
                .Select(p => new { p.Slug, p.UpdatedAt })
                .ToListAsync(http.RequestAborted);

            var xml = new StringBuilder();
            using (var writer = XmlWriter.Create(xml, new XmlWriterSettings
                   {
                       Indent = false,
                       Encoding = new UTF8Encoding(false),
                       OmitXmlDeclaration = false,
                       Async = false
                   }))
            {
                writer.WriteStartDocument();
                writer.WriteStartElement("urlset", "http://www.sitemaps.org/schemas/sitemap/0.9");

                // Static, always-public routes go first.
                static void WriteUrl(XmlWriter w, string loc, DateTime lastmod, string changefreq, string priority)
                {
                    w.WriteStartElement("url");
                    w.WriteElementString("loc", loc);
                    w.WriteElementString("lastmod", lastmod.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                    w.WriteElementString("changefreq", changefreq);
                    w.WriteElementString("priority", priority);
                    w.WriteEndElement();
                }

                var today = DateTime.UtcNow;
                WriteUrl(writer, $"{origin}/", today, "weekly", "1.0");
                WriteUrl(writer, $"{origin}/posts", today, "daily", "0.9");
                WriteUrl(writer, $"{origin}/verify-statement", today, "yearly", "0.4");
                WriteUrl(writer, $"{origin}/privacy", today, "yearly", "0.4");

                foreach (var p in posts)
                {
                    WriteUrl(writer, $"{origin}/posts/{p.Slug}", p.UpdatedAt, "monthly", "0.8");
                }

                writer.WriteEndElement(); // urlset
                writer.WriteEndDocument();
            }

            http.Response.ContentType = "application/xml; charset=utf-8";
            http.Response.Headers["Cache-Control"] = "public, max-age=3600";
            await http.Response.WriteAsync(xml.ToString(), http.RequestAborted);
        });

        app.MapGet("/robots.txt", (HttpContext http) =>
        {
            var origin = http.Request.Scheme + "://" + http.Request.Host;
            // Permissive on the public surface; admin and account flows are
            // either gated by JS-only middleware or behind login, so excluding
            // them here is purely a hint to well-behaved crawlers.
            var body =
                "User-agent: *\n" +
                "Disallow: /admin/\n" +
                "Disallow: /account\n" +
                "Disallow: /sign\n" +
                $"Sitemap: {origin}/sitemap.xml\n";
            http.Response.ContentType = "text/plain; charset=utf-8";
            http.Response.Headers["Cache-Control"] = "public, max-age=86400";
            return http.Response.WriteAsync(body, http.RequestAborted);
        });

        return app;
    }

    /// First text block's markdown, falling back to first header. Whitespace-
    /// collapsed and clipped at ~280 chars. The frontend uses the same
    /// rule for its OG description, so the feed preview matches what a link
    /// share would show. XmlWriter handles XML escaping; we don't pre-escape.
    private const int DescriptionMaxLen = 280;
    /// Public so the unit tests can hit it without an InternalsVisibleTo
    /// shim. The endpoint is the only production caller.
    public static string ExtractDescription(JsonDocument doc)
    {
        if (doc is null) return string.Empty;
        if (!doc.RootElement.TryGetProperty("blocks", out var blocks) || blocks.ValueKind != JsonValueKind.Array)
            return string.Empty;

        string? firstHeader = null;
        foreach (var b in blocks.EnumerateArray())
        {
            if (b.ValueKind != JsonValueKind.Object) continue;
            if (!b.TryGetProperty("type", out var t) || t.ValueKind != JsonValueKind.String) continue;
            if (!b.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) continue;

            if (t.GetString() == "text"
                && data.TryGetProperty("markdown", out var md)
                && md.ValueKind == JsonValueKind.String)
            {
                return Clip(md.GetString() ?? string.Empty);
            }
            if (firstHeader is null
                && t.GetString() == "header"
                && data.TryGetProperty("text", out var h)
                && h.ValueKind == JsonValueKind.String)
            {
                firstHeader = h.GetString();
            }
        }
        return Clip(firstHeader ?? string.Empty);
    }

    private static string Clip(string s)
    {
        var collapsed = string.Join(' ', s.Split(default(char[]?), StringSplitOptions.RemoveEmptyEntries));
        if (collapsed.Length <= DescriptionMaxLen) return collapsed;
        return collapsed[..(DescriptionMaxLen - 1)] + "…";
    }
}
