namespace PortfolioApi.Models;

/// A WebAuthn / FIDO2 credential bound to a user account. We persist the
/// minimum needed to verify subsequent assertions (CredentialId, PublicKey,
/// SignCount) plus a friendly Name so the user can tell their devices apart
/// in the UI.
public class Passkey
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public User? User { get; set; }

    /// Raw WebAuthn credential id. Indexed for the assertion-step lookup
    /// where the browser sends back the id and we have to find the matching
    /// row across all users.
    public byte[] CredentialId { get; set; } = Array.Empty<byte>();
    public byte[] PublicKey { get; set; } = Array.Empty<byte>();

    /// Monotonically-increasing counter the authenticator returns with each
    /// assertion. We refuse assertions whose counter doesn't strictly exceed
    /// the stored value — guards against cloned authenticators.
    public uint SignCount { get; set; }

    /// Authenticator AAGUID (vendor identifier). Optional; some authenticators
    /// return all zeros. Useful for surfacing the device family in the UI.
    public Guid Aaguid { get; set; }

    /// Names the credential. Defaults to "Passkey added on YYYY-MM-DD" but
    /// the user can rename it after enrolment.
    public string Name { get; set; } = "Passkey";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedAt { get; set; }
}
