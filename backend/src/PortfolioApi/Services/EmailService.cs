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
        var text =
            "Welcome! Please verify your email by clicking the link below:\n\n" +
            $"  {link}\n\n" +
            "If you didn't create an account, you can ignore this message.\n";
        var html = $"""
            <p>Welcome! Please verify your email by clicking the link below:</p>
            <p><a href="{link}">Verify email</a></p>
            <p>If you didn't create an account, you can ignore this message.</p>
            """;
        return SendAsync(toEmail, "Verify your email", text, html);
    }

    public Task SendPasswordResetAsync(string toEmail, string jwtToken)
    {
        var link = $"{_opt.ResetUrlBase}?token={Uri.EscapeDataString(jwtToken)}";
        var text =
            "We received a request to reset the password on this account.\n\n" +
            "Click the link below to choose a new password. The link is valid for a short time.\n\n" +
            $"  {link}\n\n" +
            "If you didn't request this, you can ignore this message — your account is unchanged.\n";
        var html = $"""
            <p>We received a request to reset the password on this account.</p>
            <p>Click the link below to choose a new password. The link is valid for a short time.</p>
            <p><a href="{link}">Reset password</a></p>
            <p>If you didn't request this, you can ignore this message — your account is unchanged.</p>
            """;
        return SendAsync(toEmail, "Reset your password", text, html);
    }

    public Task SendEmailChangeAsync(string toEmail, string jwtToken)
    {
        var link = $"{_opt.EmailChangeUrlBase}?token={Uri.EscapeDataString(jwtToken)}";
        var text =
            "Someone — hopefully you — asked to change the email address on an account to this one.\n\n" +
            "Click the link below to confirm the change. The link is valid for a short time.\n\n" +
            $"  {link}\n\n" +
            "If you didn't request this, ignore the message. The account's email address is unchanged until the link is clicked.\n";
        var html = $"""
            <p>Someone — hopefully you — asked to change the email address on an account to this one.</p>
            <p>Click the link below to confirm the change. The link is valid for a short time.</p>
            <p><a href="{link}">Confirm email change</a></p>
            <p>If you didn't request this, ignore the message. The account's email address is unchanged until the link is clicked.</p>
            """;
        return SendAsync(toEmail, "Confirm your new email", text, html);
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
        var text =
            $"The following action just happened on your account: {actionLabel}.\n" +
            $"When: {when}\n" +
            (extraNote is null ? "" : $"\n{extraNote}\n") +
            "\nIf this was you, no action is needed.\n" +
            "If it wasn't you, reset your password immediately and review the activity log on your account page.\n";
        // Subject is plain text (not HTML), but newlines would corrupt the
        // SMTP header — clamp to the first line.
        var subject = "Security alert: " + actionLabel.Replace('\n', ' ').Replace('\r', ' ');
        return SendAsync(toEmail, subject, text, html);
    }

    public Task SendCommentNotificationAsync(
        string toEmail,
        string postTitle,
        string postSlug,
        Guid commentId,
        string commenterUsername,
        string commentBody)
    {
        var (subject, text, html) = BuildCommentNotification(
            postTitle, postSlug, commentId, commenterUsername, commentBody, _opt.PostUrlBase);
        return SendAsync(toEmail, subject, text, html);
    }

    /// Pure body construction for the comment-notification email. Lifted out
    /// of the instance method so unit tests can verify HTML escaping and
    /// length clamping without an SMTP host. The returned `html` is what
    /// goes into the multipart's text/html part — assume it will be served
    /// as HTML, so any user-supplied substring must be HTML-escaped.
    public static (string Subject, string Text, string Html) BuildCommentNotification(
        string postTitle, string postSlug, Guid commentId,
        string commenterUsername, string commentBody, string postUrlBase)
    {
        // The body comes straight from a user — escape it as HTML AND clamp
        // the length so a 2KB wall doesn't dominate the email.
        const int previewLen = 280;
        var clipped = commentBody.Length > previewLen
            ? commentBody[..(previewLen - 1)] + "…"
            : commentBody;

        // Anchor the link at the comment so the author lands on the row.
        var link = $"{postUrlBase}/{Uri.EscapeDataString(postSlug)}#c-{commentId}";
        var safeTitle = HtmlEscape(postTitle);
        var safeUser = HtmlEscape(commenterUsername);
        var safeBody = HtmlEscape(clipped).Replace("\n", "<br>");
        var html = $"""
            <p><strong>{safeUser}</strong> commented on your post <em>{safeTitle}</em>:</p>
            <blockquote style="border-left: 3px solid #ccc; padding-left: 10px; color: #555;">{safeBody}</blockquote>
            <p><a href="{link}">View the comment</a></p>
            <p style="color: #888; font-size: 12px;">You can turn off these notifications on your account page.</p>
            """;
        // Plain-text alternative. Quoting the body with "> " prefixes matches
        // the convention of every text-based mail client.
        var quoted = string.Join("\n", clipped.Split('\n').Select(l => "> " + l));
        var text =
            $"{commenterUsername} commented on your post \"{postTitle}\":\n\n" +
            $"{quoted}\n\n" +
            $"View the comment: {link}\n\n" +
            "You can turn off these notifications on your account page.\n";
        var subject = "New comment on " + postTitle.Replace('\n', ' ').Replace('\r', ' ');
        return (subject, text, html);
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

    /// Builds the MimeMessage without dispatching. Public so unit tests can
    /// assert on the constructed envelope (subject, parts, escaping) without
    /// needing an SMTP server. Defaults `from` to the configured address.
    public static MimeMessage BuildMessage(string fromEmail, string toEmail, string subject, string text, string html)
    {
        if (string.IsNullOrWhiteSpace(fromEmail))
            throw new InvalidOperationException("Email:From is not configured");

        var msg = new MimeMessage();
        msg.From.Add(MailboxAddress.Parse(fromEmail));
        msg.To.Add(MailboxAddress.Parse(toEmail));
        msg.Subject = subject;
        // multipart/alternative: text-only readers (mutt, screen readers, some
        // mobile previews) take the plain part; HTML clients take the second.
        // Lower spam scores too — HTML-only mail trips a lot of filters.
        msg.Body = new MultipartAlternative
        {
            new TextPart("plain") { Text = text },
            new TextPart("html") { Text = html }
        };
        return msg;
    }

    private async Task SendAsync(string toEmail, string subject, string text, string html)
    {
        var msg = BuildMessage(_opt.From, toEmail, subject, text, html);

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
