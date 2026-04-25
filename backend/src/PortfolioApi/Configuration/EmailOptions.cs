namespace PortfolioApi.Configuration;

/// Strongly-typed binding for the "Email" configuration section.
/// SmtpHost is nullable — when not set, EmailService logs a warning and skips
/// the send rather than failing registration.
public sealed class EmailOptions
{
    public const string Section = "Email";

    public string From { get; set; } = "noreply@example.com";
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 1025;
    public string? SmtpUser { get; set; }
    public string? SmtpPassword { get; set; }
    public string VerifyUrlBase { get; set; } = "http://localhost:3000/verify";
    public string ResetUrlBase { get; set; } = "http://localhost:3000/reset-password";
    public string EmailChangeUrlBase { get; set; } = "http://localhost:3000/confirm-email-change";
    /// Used to build deep links into post pages (e.g. comment notifications).
    /// Slug + "#c-<id>" anchor is appended at the call site.
    public string PostUrlBase { get; set; } = "http://localhost:3000/posts";
}
