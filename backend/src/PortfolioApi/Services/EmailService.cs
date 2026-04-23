using MailKit.Net.Smtp;
using Microsoft.Extensions.Options;
using MimeKit;
using PortfolioApi.Configuration;

namespace PortfolioApi.Services;

public class EmailService : IEmailService
{
    private readonly EmailOptions _opt;
    private readonly ILogger<EmailService> _log;

    public EmailService(IOptions<EmailOptions> opt, ILogger<EmailService> log)
    {
        _opt = opt.Value;
        _log = log;
    }

    public Task SendVerificationAsync(string toEmail, string jwtToken)
    {
        var link = $"{_opt.VerifyUrlBase}?token={Uri.EscapeDataString(jwtToken)}";
        var html = $"""
            <p>Welcome! Please verify your email by clicking the link below:</p>
            <p><a href="{link}">Verify email</a></p>
            <p>If you didn't create an account, you can ignore this message.</p>
            """;
        return SendAsync(toEmail, "Verify your email", html);
    }

    public Task SendPasswordResetAsync(string toEmail, string jwtToken)
    {
        var link = $"{_opt.ResetUrlBase}?token={Uri.EscapeDataString(jwtToken)}";
        var html = $"""
            <p>We received a request to reset the password on this account.</p>
            <p>Click the link below to choose a new password. The link is valid for a short time.</p>
            <p><a href="{link}">Reset password</a></p>
            <p>If you didn't request this, you can ignore this message — your account is unchanged.</p>
            """;
        return SendAsync(toEmail, "Reset your password", html);
    }

    private async Task SendAsync(string toEmail, string subject, string html)
    {
        if (string.IsNullOrWhiteSpace(_opt.From))
            throw new InvalidOperationException("Email:From is not configured");

        var msg = new MimeMessage();
        msg.From.Add(MailboxAddress.Parse(_opt.From));
        msg.To.Add(MailboxAddress.Parse(toEmail));
        msg.Subject = subject;
        msg.Body = new TextPart("html") { Text = html };

        if (string.IsNullOrWhiteSpace(_opt.SmtpHost))
        {
            // No SMTP configured — log a structured event without the address or
            // the link itself (links carry an auth token; never log them).
            _log.LogWarning("SMTP not configured; email '{Subject}' not sent", subject);
            return;
        }

        try
        {
            using var client = new SmtpClient();
            await client.ConnectAsync(_opt.SmtpHost, _opt.SmtpPort, MailKit.Security.SecureSocketOptions.Auto);

            if (!string.IsNullOrEmpty(_opt.SmtpUser) && _opt.SmtpPassword is not null)
                await client.AuthenticateAsync(_opt.SmtpUser, _opt.SmtpPassword);

            await client.SendAsync(msg);
            await client.DisconnectAsync(true);
        }
        catch (Exception ex)
        {
            // Don't include the email or token in the log — only that a send failed.
            _log.LogError(ex, "SMTP send failed for '{Subject}'", subject);
        }
    }
}
