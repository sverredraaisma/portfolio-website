namespace PortfolioApi.Models;

/// One row per (user, post) pair the user has saved. The row IS the bookmark
/// — there's no per-row metadata beyond the timestamp, so a unique index on
/// (UserId, PostId) keeps add/remove idempotent.
public class Bookmark
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public User? User { get; set; }

    public Guid PostId { get; set; }
    public Post? Post { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
