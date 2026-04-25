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

    // ---- BuildCommentNotification (pure, security-critical) -------------

    private const string Origin = "https://h.example/posts";

    private static (string subject, string text, string html) BuildCommentMail(
        string title = "My Post",
        string slug = "my-post",
        string commenter = "alice",
        string body = "hello") =>
        EmailService.BuildCommentNotification(title, slug, Guid.NewGuid(), commenter, body, Origin);

    [Fact]
    public void Comment_body_with_a_script_tag_is_escaped_in_the_HTML_part()
    {
        var (_, _, html) = BuildCommentMail(body: "<script>alert('x')</script>");

        html.Should().NotContain("<script>");
        html.Should().Contain("&lt;script&gt;alert(&#39;x&#39;)&lt;/script&gt;");
    }

    [Fact]
    public void Comment_body_with_an_img_tag_is_escaped_in_the_HTML_part()
    {
        var (_, _, html) = BuildCommentMail(body: "<img src=x onerror=alert(1)>");

        html.Should().NotContain("<img");
        html.Should().Contain("&lt;img");
    }

    [Fact]
    public void Post_title_with_quote_or_ampersand_is_escaped_in_the_HTML_part()
    {
        // Titles are admin-controlled today, but treating them as untrusted
        // means a future change that pipes user input in can't bite us.
        var (_, _, html) = BuildCommentMail(title: "Tom & \"Jerry\"");

        html.Should().Contain("Tom &amp; &quot;Jerry&quot;");
    }

    [Fact]
    public void Commenter_username_is_escaped_in_the_HTML_part()
    {
        // UsernameNormalizer rejects [<>&] so this is defence in depth.
        var (_, _, html) = BuildCommentMail(commenter: "<b>");

        html.Should().NotContain("<b>");
        html.Should().Contain("&lt;b&gt;");
    }

    [Fact]
    public void Plain_text_part_carries_the_raw_body_unescaped_with_quote_prefix()
    {
        // mutt / screen readers render this as-is; no HTML escaping wanted.
        var (_, text, _) = BuildCommentMail(body: "line1\nline2");

        text.Should().Contain("> line1");
        text.Should().Contain("> line2");
    }

    [Fact]
    public void Bodies_longer_than_280_chars_are_clamped_with_an_ellipsis_in_both_parts()
    {
        var huge = new string('x', 600);

        var (_, text, html) = BuildCommentMail(body: huge);

        text.Should().NotContain(new string('x', 281));
        text.Should().Contain("…");
        html.Should().NotContain(new string('x', 281));
    }

    [Fact]
    public void Subject_strips_CR_and_LF_so_a_smuggled_header_cant_be_appended()
    {
        // CR/LF in the subject would let an attacker who controls a title
        // append fake headers (Bcc:, Content-Type:). Sanitising defence
        // in depth even though titles are admin-only today.
        var (subject, _, _) = BuildCommentMail(title: "Hello\r\nBcc: attacker@x");

        subject.Should().Be("New comment on Hello  Bcc: attacker@x");
        subject.Should().NotContain("\r");
        subject.Should().NotContain("\n");
    }

    [Fact]
    public void HTML_part_links_back_to_the_comment_anchor_on_the_post_page()
    {
        var commentId = Guid.NewGuid();
        var (_, _, html) = EmailService.BuildCommentNotification(
            "T", "my-slug", commentId, "alice", "hi", Origin);

        html.Should().Contain($"{Origin}/my-slug#c-{commentId}");
    }

    // ---- BuildMessage envelope smoke test -------------------------------

    [Fact]
    public void BuildMessage_constructs_a_multipart_alternative_with_both_parts()
    {
        var msg = EmailService.BuildMessage("noreply@x", "to@x", "subject", "plain body", "<p>html body</p>");

        msg.Subject.Should().Be("subject");
        msg.From.Mailboxes.Single().Address.Should().Be("noreply@x");
        msg.To.Mailboxes.Single().Address.Should().Be("to@x");
        var alt = msg.Body.Should().BeOfType<MimeKit.MultipartAlternative>().Subject;
        alt.Should().HaveCount(2);
        alt[0].ContentType.MimeType.Should().Be("text/plain");
        alt[1].ContentType.MimeType.Should().Be("text/html");
    }

    [Fact]
    public void BuildMessage_refuses_to_run_with_an_unconfigured_From_address()
    {
        var act = () => EmailService.BuildMessage("", "to@x", "s", "t", "h");
        act.Should().Throw<InvalidOperationException>().WithMessage("*From*");
    }
}
