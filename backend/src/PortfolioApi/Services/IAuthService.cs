using PortfolioApi.Models;

namespace PortfolioApi.Services;

public record AuthTokens(string AccessToken, string RefreshToken);

public interface IAuthService
{
    Task<User> RegisterAsync(string username, string email, string clientHashHex, CancellationToken cancellationToken = default);
    Task<User?> LoginAsync(string username, string clientHashHex, CancellationToken cancellationToken = default);
    Task<bool> VerifyEmailAsync(string jwtToken, CancellationToken cancellationToken = default);

    /// Re-issues a verification email if an unverified account exists at <paramref name="email"/>.
    /// Silently no-ops for unknown or already-verified addresses to avoid leaking
    /// account state.
    Task ResendVerificationAsync(string email, CancellationToken cancellationToken = default);

    /// Verifies the current password, sets a new one, and revokes every active
    /// refresh token for the user so any other session is signed out. Throws
    /// AuthFailedException if the current password is wrong.
    Task ChangePasswordAsync(Guid userId, string currentClientHash, string newClientHash, CancellationToken cancellationToken = default);

    /// Revokes every active refresh token for the user — "sign out everywhere".
    Task RevokeAllSessionsAsync(Guid userId, CancellationToken cancellationToken = default);

    /// Issues a refresh token. Returns the raw token (returned to the client once)
    /// and the persisted record (only the SHA-256 of the token is stored).
    Task<(string token, RefreshToken stored)> IssueRefreshTokenAsync(Guid userId, CancellationToken cancellationToken = default);

    /// Validates a raw refresh token, rotates it (revokes the old one and issues
    /// a new one), and returns a fresh access+refresh pair.
    /// Throws AuthFailedException if the token is unknown, revoked, or expired.
    Task<(AuthTokens tokens, User user)> RefreshAsync(string rawRefreshToken, CancellationToken cancellationToken = default);

    /// Revokes the supplied refresh token. Idempotent — unknown tokens are ignored.
    Task LogoutAsync(string rawRefreshToken, CancellationToken cancellationToken = default);

    /// Sends a password-reset email if a user exists at <paramref name="email"/>.
    /// Returns silently otherwise to avoid leaking which addresses have accounts.
    Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken = default);

    /// Validates the password-reset JWT, replaces the user's password hash with
    /// a fresh Argon2 over <paramref name="clientHashHex"/>, and revokes every
    /// active refresh token for the user (forcing re-login everywhere).
    /// Throws AuthFailedException if the token is invalid, expired, or carries
    /// the wrong purpose.
    Task ResetPasswordAsync(string jwtToken, string clientHashHex, CancellationToken cancellationToken = default);

    /// If the Users table is empty, creates an admin account with the supplied
    /// username/email and a random un-disclosed password. The owner gains access
    /// by triggering the password-reset flow against the email address. Returns
    /// true when the seed ran, false when an account already existed.
    Task<bool> SeedAdminIfEmptyAsync(string username, string email, CancellationToken cancellationToken = default);
}
