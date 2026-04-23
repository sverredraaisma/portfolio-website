using PortfolioApi.Models;

namespace PortfolioApi.Services;

public record AuthTokens(string AccessToken, string RefreshToken);

public interface IAuthService
{
    Task<User> RegisterAsync(string username, string email, string clientHashHex, CancellationToken cancellationToken = default);
    Task<User?> LoginAsync(string username, string clientHashHex, CancellationToken cancellationToken = default);
    Task<bool> VerifyEmailAsync(string jwtToken, CancellationToken cancellationToken = default);

    /// Issues a refresh token. Returns the raw token (returned to the client once)
    /// and the persisted record (only the SHA-256 of the token is stored).
    Task<(string token, RefreshToken stored)> IssueRefreshTokenAsync(Guid userId, CancellationToken cancellationToken = default);

    /// Validates a raw refresh token, rotates it (revokes the old one and issues
    /// a new one), and returns a fresh access+refresh pair.
    /// Throws AuthFailedException if the token is unknown, revoked, or expired.
    Task<(AuthTokens tokens, User user)> RefreshAsync(string rawRefreshToken, CancellationToken cancellationToken = default);

    /// Revokes the supplied refresh token. Idempotent — unknown tokens are ignored.
    Task LogoutAsync(string rawRefreshToken, CancellationToken cancellationToken = default);
}
