using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PortfolioApi.Data;
using PortfolioApi.Models;
using PortfolioApi.Rpc.Methods;
using PortfolioApi.Services;
using PortfolioApi.Tests.Infrastructure;

namespace PortfolioApi.Tests.Rpc;

public class CommentMethodsTests
{
    private static (CommentMethods sut, AppDbContext db, User author, User other, Post post, FakeEmail email) Setup(
        bool authorWantsNotifications = true,
        bool authorEmailVerified = true)
    {
        var test = new TestDb();
        var author = new User
        {
            Username = "alice",
            Email = "a@x",
            PasswordHash = new byte[] { 1 },
            PasswordSalt = new byte[] { 1 },
            NotifyOnComment = authorWantsNotifications,
            EmailVerifiedAt = authorEmailVerified ? DateTime.UtcNow : null
        };
        var other = new User { Username = "bob", Email = "b@x", PasswordHash = new byte[]{1}, PasswordSalt = new byte[]{1} };
        test.Db.Users.AddRange(author, other);
        // Published = true by default so the new "hide comments on
        // unpublished posts" filter doesn't accidentally hide every
        // comment seeded by the existing tests.
        var post = new Post { Title = "t", Slug = "t", AuthorId = author.Id, Published = true };
        test.Db.Posts.Add(post);
        test.Db.SaveChanges();
        test.Db.ChangeTracker.Clear();
        var email = new FakeEmail();
        var sut = new CommentMethods(test.Db, new CommentThrottle(), email, NullLogger<CommentMethods>.Instance);
        return (sut, test.Db, author, other, post, email);
    }

    [Fact]
    public async Task Create_requires_a_signed_in_user()
    {
        var (sut, _, _, _, post, _) = Setup();

        var act = async () => await sut.Create(
            new CreateCommentParams { PostId = post.Id, Body = "hi" },
            TestRpcContext.Anonymous());

        await act.Should().ThrowAsync<AuthFailedException>();
    }

    [Fact]
    public async Task Create_rejects_an_empty_or_whitespace_body()
    {
        var (sut, _, author, _, post, _) = Setup();

        var act = async () => await sut.Create(
            new CreateCommentParams { PostId = post.Id, Body = "   " },
            TestRpcContext.User(author.Id));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("body required");
    }

    [Fact]
    public async Task Create_rejects_a_body_longer_than_2000_chars()
    {
        var (sut, _, author, _, post, _) = Setup();

        var act = async () => await sut.Create(
            new CreateCommentParams { PostId = post.Id, Body = new string('x', 2001) },
            TestRpcContext.User(author.Id));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("body too long");
    }

    [Fact]
    public async Task Create_returns_the_dto_with_AuthorIsAdmin_reflecting_the_caller()
    {
        var (sut, db, _, _, post, _) = Setup();
        // Promote the author so the projection has to surface IsAdmin.
        var u = await db.Users.FirstAsync(x => x.Username == "alice");
        u.IsAdmin = true;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var c = await sut.Create(
            new CreateCommentParams { PostId = post.Id, Body = "hi" },
            TestRpcContext.User(u.Id, isAdmin: true));

        c.AuthorIsAdmin.Should().BeTrue();
        c.Author.Should().Be("alice");
    }

    [Fact]
    public async Task List_renders_an_anonymised_comment_as_anonymous_with_no_admin_marker()
    {
        var (sut, db, author, _, post, _) = Setup();
        // Anonymised: AuthorId = NULL.
        db.Comments.Add(new Comment { PostId = post.Id, AuthorId = null, Body = "from a former user" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var page = await sut.List(new ListCommentsParams { PostId = post.Id }, TestRpcContext.Anonymous());

        page.Items.Should().HaveCount(1);
        page.Items[0].Author.Should().Be("anonymous");
        page.Items[0].AuthorIsAdmin.Should().BeFalse();
    }

    [Fact]
    public async Task Create_refuses_a_non_author_comment_on_a_draft_post()
    {
        // Symmetric with comments.list: a non-author who knows the postId
        // of a draft must not be able to write into its thread either.
        // The wire shape matches posts.get's not-found response so the
        // post's existence isn't disclosed.
        var (sut, db, _, other, post, _) = Setup();
        var live = await db.Posts.SingleAsync(p => p.Id == post.Id);
        live.Published = false;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var act = async () => await sut.Create(
            new CreateCommentParams { PostId = post.Id, Body = "leaks?" },
            TestRpcContext.User(other.Id));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Post not found");
        (await db.Comments.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Create_lets_the_author_comment_on_their_own_draft_for_editor_preview_use()
    {
        // The author's editor flow may want to round-trip a comment to
        // exercise the rendering — the draft gate exempts them so the
        // preview path keeps working.
        var (sut, db, author, _, post, _) = Setup();
        var live = await db.Posts.SingleAsync(p => p.Id == post.Id);
        live.Published = false;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dto = await sut.Create(
            new CreateCommentParams { PostId = post.Id, Body = "preview" },
            TestRpcContext.User(author.Id));

        dto.Body.Should().Be("preview");
        (await db.Comments.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task List_hides_comments_on_unpublished_posts_so_drafts_dont_leak_via_postId()
    {
        // posts.get already 404s an unpublished post for non-authors —
        // comments.list must follow suit, otherwise a caller who saved
        // the postId before the post was unpublished could keep pulling
        // the discussion.
        var (sut, db, author, _, post, _) = Setup();
        db.Comments.Add(new Comment { PostId = post.Id, AuthorId = author.Id, Body = "still here?" });
        var live = await db.Posts.SingleAsync(p => p.Id == post.Id);
        live.Published = false;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var page = await sut.List(new ListCommentsParams { PostId = post.Id }, TestRpcContext.Anonymous());

        page.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Update_is_author_only_admins_cant_rewrite_someone_elses_words()
    {
        var (sut, db, author, other, post, _) = Setup();
        var c = new Comment { PostId = post.Id, AuthorId = author.Id, Body = "original" };
        db.Comments.Add(c);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var act = async () => await sut.Update(
            new UpdateCommentParams { Id = c.Id, Body = "tampered" },
            TestRpcContext.User(other.Id, isAdmin: true));

        await act.Should().ThrowAsync<AuthFailedException>();
    }

    [Fact]
    public async Task Update_lets_the_author_rewrite_their_own_comment_and_stamps_UpdatedAt()
    {
        var (sut, db, author, _, post, _) = Setup();
        var c = new Comment { PostId = post.Id, AuthorId = author.Id, Body = "first draft" };
        db.Comments.Add(c);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dto = await sut.Update(
            new UpdateCommentParams { Id = c.Id, Body = "second draft" },
            TestRpcContext.User(author.Id));

        dto.Body.Should().Be("second draft");
        dto.UpdatedAt.Should().NotBeNull("the wire payload carries the edit stamp so the frontend can show (edited)");
        var stored = await db.Comments.SingleAsync();
        stored.Body.Should().Be("second draft");
        stored.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Update_with_an_identical_body_does_not_stamp_UpdatedAt()
    {
        // A no-op save — trimmed body matches what's already stored — must
        // not light up the (edited) marker.
        var (sut, db, author, _, post, _) = Setup();
        var c = new Comment { PostId = post.Id, AuthorId = author.Id, Body = "hello" };
        db.Comments.Add(c);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        // Whitespace round it; the SUT trims before comparing, so this
        // resolves to the same byte-equal "hello" already in storage.
        var dto = await sut.Update(
            new UpdateCommentParams { Id = c.Id, Body = "  hello  " },
            TestRpcContext.User(author.Id));

        dto.UpdatedAt.Should().BeNull("a body that trims to the stored value isn't an edit");
        (await db.Comments.SingleAsync()).UpdatedAt.Should().BeNull();
    }

    [Fact]
    public async Task Delete_lets_the_author_delete_their_own()
    {
        var (sut, db, author, _, post, _) = Setup();
        var c = new Comment { PostId = post.Id, AuthorId = author.Id, Body = "x" };
        db.Comments.Add(c);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await sut.Delete(new DeleteCommentParams { Id = c.Id }, TestRpcContext.User(author.Id));

        (await db.Comments.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Delete_lets_an_admin_moderate_anyone_elses_comment()
    {
        var (sut, db, author, other, post, _) = Setup();
        var c = new Comment { PostId = post.Id, AuthorId = author.Id, Body = "x" };
        db.Comments.Add(c);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await sut.Delete(new DeleteCommentParams { Id = c.Id }, TestRpcContext.User(other.Id, isAdmin: true));

        (await db.Comments.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Delete_rejects_a_non_admin_trying_to_delete_someone_elses_comment()
    {
        var (sut, db, author, other, post, _) = Setup();
        var c = new Comment { PostId = post.Id, AuthorId = author.Id, Body = "x" };
        db.Comments.Add(c);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var act = async () => await sut.Delete(new DeleteCommentParams { Id = c.Id }, TestRpcContext.User(other.Id));

        await act.Should().ThrowAsync<AuthFailedException>();
    }

    [Fact]
    public async Task ListAll_is_admin_only()
    {
        var (sut, _, author, _, _, _) = Setup();

        var act = async () => await sut.ListAll(new ListAllCommentsParams(), TestRpcContext.User(author.Id));

        await act.Should().ThrowAsync<AuthFailedException>();
    }

    [Fact]
    public async Task Create_emails_the_post_author_when_a_different_user_comments()
    {
        var (sut, _, _, other, post, email) = Setup();

        var dto = await sut.Create(
            new CreateCommentParams { PostId = post.Id, Body = "nice post" },
            TestRpcContext.User(other.Id));

        email.Sent.Should().HaveCount(1);
        var sent = email.Sent[0];
        sent.ToEmail.Should().Be("a@x");
        sent.PostSlug.Should().Be("t");
        sent.CommenterUsername.Should().Be("bob");
        sent.CommentId.Should().Be(dto.Id);
    }

    [Fact]
    public async Task Create_does_not_email_when_the_commenter_IS_the_post_author()
    {
        // No reason to spam yourself with mail about your own comment.
        var (sut, _, author, _, post, email) = Setup();

        await sut.Create(
            new CreateCommentParams { PostId = post.Id, Body = "self-reply" },
            TestRpcContext.User(author.Id));

        email.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_does_not_email_when_the_author_has_opted_out()
    {
        var (sut, _, _, other, post, email) = Setup(authorWantsNotifications: false);

        await sut.Create(
            new CreateCommentParams { PostId = post.Id, Body = "hi" },
            TestRpcContext.User(other.Id));

        email.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_does_not_email_when_the_author_has_an_unverified_email()
    {
        // Sending to an unverified address risks spamming a stranger who
        // doesn't actually own the mailbox claimed at registration.
        var (sut, _, _, other, post, email) = Setup(authorEmailVerified: false);

        await sut.Create(
            new CreateCommentParams { PostId = post.Id, Body = "hi" },
            TestRpcContext.User(other.Id));

        email.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_succeeds_even_when_the_notification_email_throws()
    {
        // The comment is already persisted before the email fires; a flaky
        // SMTP run must not surface as a failed comment.
        var (sut, db, _, other, post, email) = Setup();
        email.Throw = true;

        var dto = await sut.Create(
            new CreateCommentParams { PostId = post.Id, Body = "still works" },
            TestRpcContext.User(other.Id));

        dto.Body.Should().Be("still works");
        (await db.Comments.CountAsync()).Should().Be(1);
    }
}

internal sealed record SentNotification(
    string ToEmail,
    string PostTitle,
    string PostSlug,
    Guid CommentId,
    string CommenterUsername,
    string CommentBody);

internal sealed class FakeEmail : IEmailService
{
    public List<SentNotification> Sent { get; } = new();
    public bool Throw { get; set; }

    public Task SendVerificationAsync(string toEmail, string jwtToken) => Task.CompletedTask;
    public Task SendPasswordResetAsync(string toEmail, string jwtToken) => Task.CompletedTask;
    public Task SendEmailChangeAsync(string toEmail, string jwtToken) => Task.CompletedTask;
    public Task SendSecurityAlertAsync(string toEmail, string actionLabel, string? extraNote = null) => Task.CompletedTask;

    public Task SendCommentNotificationAsync(string toEmail, string postTitle, string postSlug, Guid commentId, string commenterUsername, string commentBody)
    {
        if (Throw) throw new InvalidOperationException("smtp down");
        Sent.Add(new SentNotification(toEmail, postTitle, postSlug, commentId, commenterUsername, commentBody));
        return Task.CompletedTask;
    }
}
