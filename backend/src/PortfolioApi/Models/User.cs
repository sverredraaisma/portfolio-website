namespace PortfolioApi.Models;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    // Argon2id(clientHash, PasswordSalt). clientHash = sha256(password) computed in the browser.
    public byte[] PasswordHash { get; set; } = Array.Empty<byte>();
    public byte[] PasswordSalt { get; set; } = Array.Empty<byte>();

    public DateTime? EmailVerifiedAt { get; set; }
    /// Timestamp of the most recently sent verification link. Used purely
    /// for surfacing "link expires at..." on the account UI; the JWT itself
    /// carries the authoritative expiry.
    public DateTime? EmailVerifySentAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// Admin users are the only ones who can create/edit/delete posts.
    /// Set on the seeded owner; manually flipped via SQL or a follow-up admin tool.
    public bool IsAdmin { get; set; }

    /// Raw HMAC-SHA1 TOTP secret (RFC 6238). Set when the user starts an
    /// enrolment; only treated as enforcing once TotpEnabledAt is non-null.
    /// A pending-but-not-confirmed secret is kept here too — the login code
    /// path checks the enabled flag, not the presence of the secret.
    public byte[]? TotpSecret { get; set; }

    /// Timestamp when the user verified the TOTP enrolment. NULL means the
    /// secret (if any) is just a draft and login does not require a code.
    public DateTime? TotpEnabledAt { get; set; }

    public List<Post> Posts { get; set; } = new();
    public List<Comment> Comments { get; set; } = new();
}
