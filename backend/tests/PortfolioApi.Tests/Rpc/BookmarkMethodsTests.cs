using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PortfolioApi.Data;
using PortfolioApi.Models;
using PortfolioApi.Rpc.Methods;
using PortfolioApi.Services;
using PortfolioApi.Tests.Infrastructure;

namespace PortfolioApi.Tests.Rpc;

public class BookmarkMethodsTests
{
    private static (BookmarkMethods sut, AppDbContext db, User reader, Post post, Post draft) Setup()
    {
        var test = new TestDb();
        var author = new User { Username = "alice", Email = "a@x", PasswordHash = new byte[]{1}, PasswordSalt = new byte[]{1} };
        var reader = new User { Username = "bob",   Email = "b@x", PasswordHash = new byte[]{1}, PasswordSalt = new byte[]{1} };
        test.Db.Users.AddRange(author, reader);
        var post = new Post
        {
            Title = "Live", Slug = "live", AuthorId = author.Id, Published = true,
            Blocks = JsonDocument.Parse("{\"blocks\":[]}")
        };
        var draft = new Post
        {
            Title = "Draft", Slug = "draft", AuthorId = author.Id, Published = false,
            Blocks = JsonDocument.Parse("{\"blocks\":[]}")
        };
        test.Db.Posts.AddRange(post, draft);
        test.Db.SaveChanges();
        test.Db.ChangeTracker.Clear();
        return (new BookmarkMethods(test.Db), test.Db, reader, post, draft);
    }

    [Fact]
    public async Task Toggle_requires_a_signed_in_caller()
    {
        var (sut, _, _, post, _) = Setup();

        var act = async () => await sut.Toggle(
            new ToggleBookmarkParams { PostId = post.Id }, TestRpcContext.Anonymous());

        await act.Should().ThrowAsync<AuthFailedException>();
    }

    [Fact]
    public async Task List_requires_a_signed_in_caller()
    {
        // Listing someone's bookmarks is per-user — anonymous callers
        // can't even pose the question.
        var (sut, _, _, _, _) = Setup();

        var act = async () => await sut.List(TestRpcContext.Anonymous());

        await act.Should().ThrowAsync<AuthFailedException>();
    }

    [Fact]
    public async Task Toggle_first_call_adds_the_bookmark_and_returns_IsBookmarked_true()
    {
        var (sut, db, reader, post, _) = Setup();

        var res = await sut.Toggle(
            new ToggleBookmarkParams { PostId = post.Id }, TestRpcContext.User(reader.Id));

        res.IsBookmarked.Should().BeTrue();
        (await db.Bookmarks.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Toggle_second_call_removes_the_bookmark_and_returns_IsBookmarked_false()
    {
        var (sut, db, reader, post, _) = Setup();
        await sut.Toggle(new ToggleBookmarkParams { PostId = post.Id }, TestRpcContext.User(reader.Id));

        var res = await sut.Toggle(new ToggleBookmarkParams { PostId = post.Id }, TestRpcContext.User(reader.Id));

        res.IsBookmarked.Should().BeFalse();
        (await db.Bookmarks.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Toggle_for_an_unknown_post_throws_not_found_without_creating_a_bookmark()
    {
        var (sut, db, reader, _, _) = Setup();

        var act = async () => await sut.Toggle(
            new ToggleBookmarkParams { PostId = Guid.NewGuid() }, TestRpcContext.User(reader.Id));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Post not found");
        (await db.Bookmarks.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Bookmarks_from_two_users_on_the_same_post_are_independent()
    {
        // The unique index covers (UserId, PostId), not just PostId — two
        // different users must be able to bookmark the same post.
        var (sut, db, reader, post, _) = Setup();
        var other = await db.Users.AddAsync(new User { Username = "carol", Email = "c@x", PasswordHash = new byte[]{1}, PasswordSalt = new byte[]{1} });
        await db.SaveChangesAsync();

        await sut.Toggle(new ToggleBookmarkParams { PostId = post.Id }, TestRpcContext.User(reader.Id));
        await sut.Toggle(new ToggleBookmarkParams { PostId = post.Id }, TestRpcContext.User(other.Entity.Id));

        (await db.Bookmarks.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task List_returns_only_the_callers_published_bookmarks_newest_first()
    {
        var (sut, db, reader, post, draft) = Setup();
        // Bookmark a draft + the published post, then a second published
        // post created later — list should return both published rows in
        // newest-first order, draft excluded.
        var second = new Post
        {
            Title = "Second", Slug = "second", AuthorId = post.AuthorId, Published = true,
            Blocks = JsonDocument.Parse("{\"blocks\":[]}"),
            CreatedAt = DateTime.UtcNow.AddMinutes(1)
        };
        db.Posts.Add(second);
        db.Bookmarks.AddRange(
            new Bookmark { UserId = reader.Id, PostId = post.Id,   CreatedAt = new DateTime(2026, 1, 1) },
            new Bookmark { UserId = reader.Id, PostId = draft.Id,  CreatedAt = new DateTime(2026, 1, 2) },
            new Bookmark { UserId = reader.Id, PostId = second.Id, CreatedAt = new DateTime(2026, 1, 3) });
        await db.SaveChangesAsync();

        var rows = await sut.List(TestRpcContext.User(reader.Id));

        rows.Should().HaveCount(2, "draft bookmarks are filtered out — clicking would 404");
        rows.Select(r => r.PostSlug).Should().Equal("second", "live");
    }

    [Fact]
    public async Task List_only_surfaces_the_callers_own_rows_never_someone_elses()
    {
        var (sut, db, reader, post, _) = Setup();
        var other = new User { Username = "carol", Email = "c@x", PasswordHash = new byte[]{1}, PasswordSalt = new byte[]{1} };
        db.Users.Add(other);
        await db.SaveChangesAsync();
        db.Bookmarks.AddRange(
            new Bookmark { UserId = reader.Id, PostId = post.Id },
            new Bookmark { UserId = other.Id,  PostId = post.Id });
        await db.SaveChangesAsync();

        var rows = await sut.List(TestRpcContext.User(reader.Id));

        rows.Should().HaveCount(1);
        rows[0].PostSlug.Should().Be("live");
    }

    [Fact]
    public async Task IsBookmarked_returns_false_for_anonymous_callers_without_hitting_the_DB()
    {
        var (sut, _, _, post, _) = Setup();

        var res = await sut.IsBookmarked(
            new ToggleBookmarkParams { PostId = post.Id }, TestRpcContext.Anonymous());

        res.IsBookmarked.Should().BeFalse();
    }

    [Fact]
    public async Task IsBookmarked_reflects_the_current_state_for_the_caller()
    {
        var (sut, _, reader, post, _) = Setup();
        await sut.Toggle(new ToggleBookmarkParams { PostId = post.Id }, TestRpcContext.User(reader.Id));

        var res = await sut.IsBookmarked(
            new ToggleBookmarkParams { PostId = post.Id }, TestRpcContext.User(reader.Id));

        res.IsBookmarked.Should().BeTrue();
    }
}
