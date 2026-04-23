using System.Security.Claims;

namespace PortfolioApi.Services;

public interface IJwtService
{
    string CreateAccessToken(Guid userId, string username);
    string CreateEmailVerifyToken(Guid userId, string email);

    /// Returns null if the token is invalid, expired, or carries the wrong purpose claim.
    ClaimsPrincipal? Validate(string token, string expectedPurpose);
}
