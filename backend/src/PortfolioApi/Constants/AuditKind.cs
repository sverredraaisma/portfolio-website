namespace PortfolioApi.Constants;

/// Stable short identifiers for AuditEvent.Kind. Add new entries rather than
/// renaming existing ones so old rows stay readable.
public static class AuditKind
{
    public const string PasswordChanged = "password.changed";
    public const string PasswordReset = "password.reset";
    public const string EmailChanged = "email.changed";
    public const string TotpEnabled = "totp.enabled";
    public const string TotpDisabled = "totp.disabled";
    public const string RecoveryCodesRegenerated = "totp.recoveryCodesRegenerated";
    public const string SessionsRevoked = "sessions.revoked";
    public const string PasskeyAdded = "passkey.added";
    public const string PasskeyRemoved = "passkey.removed";
    public const string LocationShared = "location.shared";
    public const string LocationUpdated = "location.updated";
    public const string LocationCleared = "location.cleared";
}
