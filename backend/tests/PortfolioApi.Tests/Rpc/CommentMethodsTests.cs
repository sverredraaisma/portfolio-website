using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PortfolioApi.Data;
using PortfolioApi.Models;
using PortfolioApi.Rpc.Methods;
using PortfolioApi.Services;
using PortfolioApi.Tests.Infrastructure;

namespace PortfolioApi.Tests.Rpc;

public class CommentMethodsTests
{
    private static (CommentMethods sut, AppDbContext db, User author, User other, Post post) Setup()
    {
        var test = new TestDb();
        var author = new User { Username = "alice", Email = "a@x", PasswordHash = new byte[]{1}, PasswordSalt = new byte[]{1} };
        var other = new User { Username = "bob", Email = "b@x", PasswordHash = new byte[]{1}, PasswordSalt = new byte[]{1} };
        test.Db.Users.AddRange(author, other);
        var post = new Post { Title = "t", Slug = "t", AuthorId = author.Id };
        test.Db.Posts.Add(post);
        test.Db.SaveChanges();
        test.Db.ChangeTracker.Clear();
        return (new CommentMethods(test.Db, new CommentThrottle()), test.Db, author, other, post);
    }

    [Fact]
    public async Task Create_requires_a_signed_in_user()
    {
        var (sut, _, _, _, post) = Setup();

        var act = async () => await sut.Create(
            new CreateCommentParams { PostId = post.Id, Body = "hi" },
            TestRpcContext.Anonymous());

        await act.Should().ThrowAsync<AuthFailedException>();
    }

    [Fact]
    public async Task Create_rejects_an_empty_or_whitespace_body()
    {
        var (sut, _, author, _, post) = Setup();

        var act = async () => await sut.Create(
            new CreateCommentParams { PostId = post.Id, Body = "   " },
            TestRpcContext.User(author.Id));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("body required");
    }

    [Fact]
    public async Task Create_rejects_a_body_longer_than_2000_chars()
    {
        var (sut, _, author, _, post) = Setup();

        var act = async () => await sut.Create(
            new CreateCommentParams { PostId = post.Id, Body = new string('x', 2001) },
            TestRpcContext.User(author.Id));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("body too long");
    }

    [Fact]
    public async Task Create_returns_the_dto_with_AuthorIsAdmin_reflecting_the_caller()
    {
        var (sut, db, _, _, post) = Setup();
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
        var (sut, db, author, _, post) = Setup();
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
    public async Task Update_is_author_only_admins_cant_rewrite_someone_elses_words()
    {
        var (sut, db, author, other, post) = Setup();
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
    public async Task Update_lets_the_author_rewrite_their_own_comment()
    {
        var (sut, db, author, _, post) = Setup();
        var c = new Comment { PostId = post.Id, AuthorId = author.Id, Body = "first draft" };
        db.Comments.Add(c);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dto = await sut.Update(
            new UpdateCommentParams { Id = c.Id, Body = "second draft" },
            TestRpcContext.User(author.Id));

        dto.Body.Should().Be("second draft");
        (await db.Comments.SingleAsync()).Body.Should().Be("second draft");
    }

    [Fact]
    public async Task Delete_lets_the_author_delete_their_own()
    {
        var (sut, db, author, _, post) = Setup();
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
        var (sut, db, author, other, post) = Setup();
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
        var (sut, db, author, other, post) = Setup();
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
        var (sut, _, author, _, _) = Setup();

        var act = async () => await sut.ListAll(new ListAllCommentsParams(), TestRpcContext.User(author.Id));

        await act.Should().ThrowAsync<AuthFailedException>();
    }
}
