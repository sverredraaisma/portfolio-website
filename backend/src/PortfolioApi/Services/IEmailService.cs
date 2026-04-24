namespace PortfolioApi.Services;

public interface IEmailService
{
    Task SendVerificationAsync(string toEmail, string jwtToken);
    Task SendPasswordResetAsync(string toEmail, string jwtToken);
    /// Sent to the *new* address as part of an email-change request: clicking
    /// the link confirms the user controls that mailbox.
    Task SendEmailChangeAsync(string toEmail, string jwtToken);
}
