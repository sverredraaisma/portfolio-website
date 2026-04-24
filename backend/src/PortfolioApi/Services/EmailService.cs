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

    public Task SendEmailChangeAsync(string toEmail, string jwtToken)
    {
        var link = $"{_opt.EmailChangeUrlBase}?token={Uri.EscapeDataString(jwtToken)}";
        var html = $"""
            <p>Someone — hopefully you — asked to change the email address on an account to this one.</p>
            <p>Click the link below to confirm the change. The link is valid for a short time.</p>
            <p><a href="{link}">Confirm email change</a></p>
            <p>If you didn't request this, ignore the message. The account's email address is unchanged until the link is clicked.</p>
            """;
        return SendAsync(toEmail, "Confirm your new email", html);
    }

    public Task SendSecurityAlertAsync(string toEmail, string actionLabel, string? extraNote = null)
    {
        var when = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'");
        var safeAction = HtmlEscape(actionLabel);
        var note = extraNote is null ? "" : $"<p>{HtmlEscape(extraNote)}</p>";
        var html = $"""
            <p>The following action just happened on your account: <strong>{safeAction}</strong>.</p>
            <p>When: {when}</p>
            {note}
            <p>If this was you, no action is needed.</p>
            <p>If it wasn't you, reset your password immediately and review the activity log on your account page.</p>
            """;
        // Subject is plain text (not HTML), but newlines would corrupt the
        // SMTP header — clamp to the first line.
        var subject = "Security alert: " + actionLabel.Replace('\n', ' ').Replace('\r', ' ');
        return SendAsync(toEmail, subject, html);
    }

    // Small inline HTML escape — actionLabel and extraNote are server-supplied
    // constants today, but treating them as untrusted means we can't be bitten
    // by a future change that pipes user input in.
    private static string HtmlEscape(string s) => s
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;")
        .Replace("'", "&#39;");

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
