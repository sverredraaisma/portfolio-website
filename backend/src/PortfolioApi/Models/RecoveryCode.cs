namespace PortfolioApi.Models;

/// One-time backup code that can substitute for a TOTP code when the user
/// loses access to their authenticator. Stored as a SHA-256 hash so a leaked
/// database backup is not a backdoor; the raw code is only shown once at
/// generation time.
public class RecoveryCode
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public User? User { get; set; }

    public byte[] CodeHash { get; set; } = Array.Empty<byte>();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UsedAt { get; set; }
}
