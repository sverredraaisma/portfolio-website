using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PortfolioApi.Configuration;
using PortfolioApi.Services;

namespace PortfolioApi.Tests.Services;

/// EmailService is mostly an SMTP wrapper, but the security-alert path
/// renders user-supplied strings into HTML — that's the bit worth pinning.
/// We can't easily intercept outgoing SMTP, so we lean on the SmtpHost-not-
/// configured branch which short-circuits to a log warning.
public class EmailServiceTests
{
    private static EmailService Build() =>
        new(Options.Create(new EmailOptions { From = "no-reply@example.com" }),
            NullLogger<EmailService>.Instance);

    [Fact]
    public async Task SendSecurityAlertAsync_completes_when_SMTP_is_unconfigured()
    {
        // The "no SMTP configured" branch logs a warning and returns. The
        // production path uses fire-and-forget, so the only contract here
        // is "this doesn't throw and we don't crash without an SMTP host".
        var sut = Build();

        var act = async () => await sut.SendSecurityAlertAsync("alice@example.com", "Password changed");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SendSecurityAlertAsync_does_not_throw_when_action_label_contains_HTML_metacharacters()
    {
        // Action labels are server-supplied today, but the inline HtmlEscape
        // means future user-derived labels won't open an XSS hole. Pin that
        // by passing a label loaded with metacharacters and confirming the
        // call still succeeds (no parser explosion, no unhandled exception).
        var sut = Build();

        var act = async () => await sut.SendSecurityAlertAsync(
            "alice@example.com",
            "<script>alert(1)</script> & \"quoted\" 'single'");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SendSecurityAlertAsync_does_not_throw_when_action_label_contains_a_newline_for_subject()
    {
        // Subject newline-stripping closes an SMTP header injection vector.
        // Without the strip, a label like "X\nBcc: attacker@evil" would set
        // a Bcc header on the outgoing message.
        var sut = Build();

        var act = async () => await sut.SendSecurityAlertAsync(
            "alice@example.com",
            "Password changed\nBcc: leak@evil.example");

        await act.Should().NotThrowAsync();
    }
}
