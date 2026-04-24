namespace PortfolioApi.Constants;

/// Values for the custom "purpose" claim on JWTs we issue.
/// Validating tokens against the wrong purpose is an instant rejection — this
/// stops, for example, an email-verify token being replayed as an access token.
public static class JwtPurpose
{
    public const string Access = "access";
    public const string EmailVerify = "email-verify";
    public const string PasswordReset = "password-reset";
    /// Embedded in tokens minted by auth.requestEmailChange. The token's "sub"
    /// is the userId; the desired new address travels as the "email" claim.
    public const string EmailChange = "email-change";
    /// Short-lived ticket issued after a successful password check when the
    /// user has TOTP enabled. Carries the userId; client posts it back with
    /// a TOTP code to auth.completeTotp to actually obtain a session.
    public const string TotpChallenge = "totp-challenge";
}
