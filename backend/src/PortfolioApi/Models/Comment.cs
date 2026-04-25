namespace PortfolioApi.Models;

public class Comment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// Set when the author edits the body via comments.update. Null means
    /// "never edited" — the frontend uses presence to show the (edited)
    /// marker and the value as the tooltip timestamp.
    public DateTime? UpdatedAt { get; set; }

    public Guid PostId { get; set; }
    public Post? Post { get; set; }

    // Nullable so a user can leave their comments behind (anonymised) when
    // they delete their account. NULL is rendered as "anonymous" client-side.
    public Guid? AuthorId { get; set; }
    public User? Author { get; set; }
}
