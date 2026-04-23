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
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Guid AuthorId { get; set; }
    public User? Author { get; set; }

    public List<Comment> Comments { get; set; } = new();
}
