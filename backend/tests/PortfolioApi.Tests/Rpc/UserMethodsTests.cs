using System.Text.Json;
using FluentAssertions;
using PortfolioApi.Data;
using PortfolioApi.Models;
using PortfolioApi.Rpc.Methods;
using PortfolioApi.Tests.Infrastructure;

namespace PortfolioApi.Tests.Rpc;

public class UserMethodsTests
{
    private static (UserMethods sut, AppDbContext db, User author) Setup()
    {
        var test = new TestDb();
        var author = new User
        {
            Username = "alice",
            Email = "a@x",
            PasswordHash = new byte[] { 1 },
            PasswordSalt = new byte[] { 1 },
            IsAdmin = true
        };
        test.Db.Users.Add(author);
        test.Db.SaveChanges();
        test.Db.ChangeTracker.Clear();
        return (new UserMethods(test.Db), test.Db, author);
    }

    private static Post Post(Guid authorId, string title, string slug, bool published = true) =>
        new()
        {
            Title = title,
            Slug = slug,
            AuthorId = authorId,
            Published = published,
            Blocks = JsonDocument.Parse("{\"blocks\":[]}")
        };

    [Fact]
    public async Task Returns_basic_fields_for_an_existing_user()
    {
        var (sut, _, _) = Setup();

        var dto = await sut.GetProfile(
            new GetProfileParams { Username = "alice" },
            TestRpcContext.Anonymous());

        dto.Username.Should().Be("alice");
        dto.IsAdmin.Should().BeTrue();
        dto.PostCount.Should().Be(0);
        dto.CommentCount.Should().Be(0);
    }

    [Fact]
    public async Task Unknown_username_throws_not_found()
    {
        var (sut, _, _) = Setup();

        var act = async () => await sut.GetProfile(
            new GetProfileParams { Username = "ghost" },
            TestRpcContext.Anonymous());

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("User not found");
    }

    [Fact]
    public async Task Username_match_is_case_sensitive_to_match_the_DB_uniqueness_constraint()
    {
        // The DB unique index is case-sensitive; the lookup must agree, otherwise
        // a user named "Alice" would shadow "alice" on /u/alice.
        var (sut, _, _) = Setup();

        var act = async () => await sut.GetProfile(
            new GetProfileParams { Username = "ALICE" },
            TestRpcContext.Anonymous());

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("User not found");
    }

    [Fact]
    public async Task Empty_or_oversized_username_is_rejected_before_the_DB_lookup()
    {
        var (sut, _, _) = Setup();

        var blank = async () => await sut.GetProfile(
            new GetProfileParams { Username = "   " },
            TestRpcContext.Anonymous());
        await blank.Should().ThrowAsync<InvalidOperationException>().WithMessage("username required");

        var huge = async () => await sut.GetProfile(
            new GetProfileParams { Username = new string('a', 65) },
            TestRpcContext.Anonymous());
        await huge.Should().ThrowAsync<InvalidOperationException>().WithMessage("username too long");
    }

    [Fact]
    public async Task Post_count_excludes_drafts()
    {
        var (sut, db, author) = Setup();
        db.Posts.AddRange(
            Post(author.Id, "p1", "p1", published: true),
            Post(author.Id, "p2", "p2", published: true),
            Post(author.Id, "draft", "draft", published: false));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dto = await sut.GetProfile(new GetProfileParams { Username = "alice" }, TestRpcContext.Anonymous());

        dto.PostCount.Should().Be(2, "drafts are not visible publicly so they don't bump the public count");
        dto.RecentPosts.Select(p => p.Slug).Should().NotContain("draft");
    }

    [Fact]
    public async Task Recent_strips_are_capped_and_sorted_newest_first()
    {
        var (sut, db, author) = Setup();
        var post = Post(author.Id, "p", "p");
        db.Posts.Add(post);
        for (int i = 0; i < 7; i++)
        {
            db.Comments.Add(new Comment
            {
                PostId = post.Id,
                AuthorId = author.Id,
                Body = $"c{i}",
                CreatedAt = new DateTime(2026, 1, 1).AddMinutes(i)
            });
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dto = await sut.GetProfile(new GetProfileParams { Username = "alice" }, TestRpcContext.Anonymous());

        dto.CommentCount.Should().Be(7);
        dto.RecentComments.Should().HaveCount(5);
        dto.RecentComments.Select(c => c.Body).Should().Equal("c6", "c5", "c4", "c3", "c2");
    }

    [Fact]
    public async Task Recent_comments_exclude_those_on_drafts()
    {
        // A draft is unreachable from the public site; surfacing comments on
        // one would 404 the link and leak that the draft exists.
        var (sut, db, author) = Setup();
        var pub = Post(author.Id, "pub", "pub");
        var draft = Post(author.Id, "draft", "draft", published: false);
        db.Posts.AddRange(pub, draft);
        db.Comments.AddRange(
            new Comment { PostId = pub.Id, AuthorId = author.Id, Body = "visible" },
            new Comment { PostId = draft.Id, AuthorId = author.Id, Body = "hidden" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dto = await sut.GetProfile(new GetProfileParams { Username = "alice" }, TestRpcContext.Anonymous());

        dto.RecentComments.Select(c => c.Body).Should().BeEquivalentTo(new[] { "visible" });
    }
}
