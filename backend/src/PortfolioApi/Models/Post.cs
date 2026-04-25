using System.Text.Json;

namespace PortfolioApi.Models;

public class Post
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;

    // Block tree as JSON. See CLAUDE.md for the schema.
    public JsonDocument Blocks { get; set; } = JsonDocument.Parse("{\"blocks\":[]}");

    public bool Published { get; set; }

    /// Admin-only flag: pinned posts surface at the top of /posts ahead of
    /// the date-ordered tail. Multiple posts can be pinned; among pinned
    /// posts the standard CreatedAt-desc order applies.
    public bool IsPinned { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Guid AuthorId { get; set; }
    public User? Author { get; set; }

    public List<Comment> Comments { get; set; } = new();

    /// Free-form short labels (e.g. "rust", "design"). Stored as a normalised
    /// list; treated case-insensitively. Persisted as a Postgres text[] column
    /// rather than a join table — there are no per-tag rows or counts to keep.
    public List<string> Tags { get; set; } = new();
}
