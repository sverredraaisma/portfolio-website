using System.Globalization;
using System.Text;
using System.Xml;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using PortfolioApi.Data;

namespace PortfolioApi.Rpc;

/// RSS 2.0 feed of published posts. Lives outside the RPC router because feed
/// readers expect a plain GET against an .xml URL — a JSON-RPC endpoint won't
/// do. Bounded to the most recent 50 posts so the body never gets unwieldy.
public static class RssEndpoint
{
    private const int MaxItems = 50;

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
                .Select(p => new { p.Title, p.Slug, p.CreatedAt, Author = p.Author!.Username })
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

        return app;
    }
}
