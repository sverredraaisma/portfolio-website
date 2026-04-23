namespace PortfolioApi.Models;

public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User? User { get; set; }

    // SHA-256 of the token bytes; raw token is only ever returned once to the client.
    public byte[] TokenHash { get; set; } = Array.Empty<byte>();

    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAt { get; set; }
}
