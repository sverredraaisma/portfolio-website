namespace PortfolioApi.Models;

public class Comment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Guid PostId { get; set; }
    public Post? Post { get; set; }

    // Nullable so a user can leave their comments behind (anonymised) when
    // they delete their account. NULL is rendered as "anonymous" client-side.
    public Guid? AuthorId { get; set; }
    public User? Author { get; set; }
}
