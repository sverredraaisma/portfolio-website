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

        var msg = new MimeMessage();
        msg.From.Add(MailboxAddress.Parse(_config["Email:From"]));
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

        try
        {
            using var client = new SmtpClient();
            await client.ConnectAsync(
                _config["Email:SmtpHost"],
                int.Parse(_config["Email:SmtpPort"] ?? "1025"),
                MailKit.Security.SecureSocketOptions.Auto);

            var user = _config["Email:SmtpUser"];
            if (!string.IsNullOrEmpty(user))
                await client.AuthenticateAsync(user, _config["Email:SmtpPassword"]);

            await client.SendAsync(msg);
            await client.DisconnectAsync(true);
        }
        catch (Exception ex)
        {
            // In dev (no SMTP), log the link so the user can still verify.
            _log.LogWarning(ex, "SMTP send failed; verification link for {Email}: {Link}", toEmail, link);
        }
    }
}
