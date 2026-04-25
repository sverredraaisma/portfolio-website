namespace PortfolioApi.Services;

public interface IEmailService
{
    Task SendVerificationAsync(string toEmail, string jwtToken);
    Task SendPasswordResetAsync(string toEmail, string jwtToken);
    /// Sent to the *new* address as part of an email-change request: clicking
    /// the link confirms the user controls that mailbox.
    Task SendEmailChangeAsync(string toEmail, string jwtToken);

    /// "Was this you?" notification fired after a sensitive account action
    /// (password change, TOTP toggle, etc). The action has already happened —
    /// the email is for awareness so the user can react if it wasn't them.
    /// Send failures are swallowed; this is best-effort signal, not a guard.
    Task SendSecurityAlertAsync(string toEmail, string actionLabel, string? extraNote = null);

    /// Sent to a post author when someone leaves a comment. Best-effort —
    /// the comment is already persisted before this fires, and we never
    /// fail the create on a send error. Strings are HTML-escaped at send
    /// time; pass them in raw.
    Task SendCommentNotificationAsync(
        string toEmail,
        string postTitle,
        string postSlug,
        Guid commentId,
        string commenterUsername,
        string commentBody);
}
