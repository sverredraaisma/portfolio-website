namespace PortfolioApi.Constants;

/// Values for the custom "purpose" claim on JWTs we issue.
/// Validating tokens against the wrong purpose is an instant rejection — this
/// stops, for example, an email-verify token being replayed as an access token.
public static class JwtPurpose
{
    public const string Access = "access";
    public const string EmailVerify = "email-verify";
    public const string PasswordReset = "password-reset";
}
