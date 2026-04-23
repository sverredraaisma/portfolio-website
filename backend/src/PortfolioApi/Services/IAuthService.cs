using PortfolioApi.Models;

namespace PortfolioApi.Services;

public interface IAuthService
{
    Task<User> RegisterAsync(string username, string email, string clientHashHex);
    Task<User?> LoginAsync(string username, string clientHashHex);
    Task<bool> VerifyEmailAsync(string jwtToken);

    /// Issues a refresh token. Returns the raw token (returned to the client once)
    /// and the persisted record (only the SHA-256 of the token is stored).
    Task<(string token, RefreshToken stored)> IssueRefreshTokenAsync(Guid userId);
}
