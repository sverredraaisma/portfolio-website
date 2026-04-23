using MailKit.Net.Smtp;
using MimeKit;

namespace PortfolioApi.Services;

public class EmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _log;

    public EmailService(IConfiguration config, ILogger<EmailService> log)
    {
        _config = config;
        _log = log;
    }

    public async Task SendVerificationAsync(string toEmail, string jwtToken)
    {
        var verifyBase = _config["Email:VerifyUrlBase"] ?? "http://localhost:3000/verify";
        var link = $"{verifyBase}?token={Uri.EscapeDataString(jwtToken)}";

        var fromAddr = _config["Email:From"]
            ?? throw new InvalidOperationException("Email:From is not configured");

        var msg = new MimeMessage();
        msg.From.Add(MailboxAddress.Parse(fromAddr));
        msg.To.Add(MailboxAddress.Parse(toEmail));
        msg.Subject = "Verify your email";
        msg.Body = new TextPart("html")
        {
            Text = $"""
                <p>Welcome! Please verify your email by clicking the link below:</p>
                <p><a href="{link}">Verify email</a></p>
                <p>If you didn't create an account, you can ignore this message.</p>
                """
        };

        var smtpHost = _config["Email:SmtpHost"];
        if (string.IsNullOrWhiteSpace(smtpHost))
        {
            // No SMTP configured — log a structured event without the address or
            // the link itself (links carry an auth token; never log them).
            _log.LogWarning("SMTP not configured; verification email for user not sent");
            return;
        }

        try
        {
            using var client = new SmtpClient();
            await client.ConnectAsync(
                smtpHost,
                int.Parse(_config["Email:SmtpPort"] ?? "1025"),
                MailKit.Security.SecureSocketOptions.Auto);

            var user = _config["Email:SmtpUser"];
            var pass = _config["Email:SmtpPassword"];
            if (!string.IsNullOrEmpty(user) && pass is not null)
                await client.AuthenticateAsync(user, pass);

            await client.SendAsync(msg);
            await client.DisconnectAsync(true);
        }
        catch (Exception ex)
        {
            // Don't include the email or token in the log — only that a send
            // failed. The exception itself goes through ILogger which redacts
            // none of it, but we keep the structured fields PII-free.
            _log.LogError(ex, "SMTP send failed");
        }
    }
}
