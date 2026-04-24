using System.Security.Claims;

namespace PortfolioApi.Services;

public interface IJwtService
{
    string CreateAccessToken(Guid userId, string username, bool isAdmin);
    string CreateEmailVerifyToken(Guid userId, string email);
    string CreatePasswordResetToken(Guid userId);
    /// Token used to confirm an email-change request. Carries both the user id
    /// (sub) and the new address (email) so the confirmation handler can
    /// trust both fields without a separate database lookup.
    string CreateEmailChangeToken(Guid userId, string newEmail);

    /// 5-minute ticket bridging the password and TOTP steps of login.
    string CreateTotpChallengeToken(Guid userId);

    /// Returns null if the token is invalid, expired, or carries the wrong purpose claim.
    ClaimsPrincipal? Validate(string token, string expectedPurpose);
}
