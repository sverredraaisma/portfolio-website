using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PortfolioApi.Rpc.Methods;
using PortfolioApi.Services;
using PortfolioApi.Tests.Infrastructure;

namespace PortfolioApi.Tests.Rpc;

/// AccountMethods is a thin pass-through over IAccountService, but the
/// CommentStrategy parsing helper is its own logic and worth pinning so a
/// future typo doesn't silently flip the deletion semantics.
public class AccountMethodsTests
{
    [Theory]
    [InlineData("anonymise")]
    [InlineData("anonymize")]   // American spelling
    [InlineData("ANONYMISE")]   // case-insensitive
    [InlineData("")]            // empty defaults to anonymise (less destructive)
    public async Task Delete_with_anonymise_aliases_passes_through_as_anonymise(string raw)
    {
        var spy = new SpyAccountService();
        var sut = new AccountMethods(spy, new TestDb().Db);
        var ctx = TestRpcContext.User(Guid.NewGuid());

        await sut.Delete(new DeleteAccountParams { CommentStrategy = raw }, ctx);

        spy.LastStrategy.Should().Be(CommentDeletionStrategy.Anonymise);
    }

    [Fact]
    public async Task Delete_with_explicit_delete_strategy_passes_through()
    {
        var spy = new SpyAccountService();
        var sut = new AccountMethods(spy, new TestDb().Db);

        await sut.Delete(new DeleteAccountParams { CommentStrategy = "delete" }, TestRpcContext.User(Guid.NewGuid()));

        spy.LastStrategy.Should().Be(CommentDeletionStrategy.Delete);
    }

    [Fact]
    public async Task Delete_rejects_an_unknown_comment_strategy()
    {
        var sut = new AccountMethods(new SpyAccountService(), new TestDb().Db);

        var act = async () => await sut.Delete(
            new DeleteAccountParams { CommentStrategy = "obliterate" },
            TestRpcContext.User(Guid.NewGuid()));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*anonymise*");
    }

    [Fact]
    public async Task Export_requires_a_signed_in_caller()
    {
        var sut = new AccountMethods(new SpyAccountService(), new TestDb().Db);

        var act = async () => await sut.Export(TestRpcContext.Anonymous());

        await act.Should().ThrowAsync<AuthFailedException>();
    }

    // ---- SetBio ---------------------------------------------------------

    [Fact]
    public async Task SetBio_persists_a_trimmed_value_for_the_caller()
    {
        var test = new TestDb();
        var u = new PortfolioApi.Models.User
        {
            Username = "alice", Email = "a@x",
            PasswordHash = new byte[] { 1 }, PasswordSalt = new byte[] { 1 }
        };
        test.Db.Users.Add(u);
        await test.Db.SaveChangesAsync();
        var sut = new AccountMethods(new SpyAccountService(), test.Db);

        await sut.SetBio(new SetBioParams { Bio = "  Hi from Alice.  " }, TestRpcContext.User(u.Id));

        // ExecuteUpdateAsync bypasses the change-tracker, so we have to
        // dodge the cached pre-load by reading via AsNoTracking.
        (await test.Db.Users.AsNoTracking().SingleAsync()).Bio.Should().Be("Hi from Alice.");
    }

    [Fact]
    public async Task SetBio_rejects_an_oversized_value_with_a_user_facing_error()
    {
        // 280-char ceiling matches the DB column max so the second line of
        // defence (constraint violation) never fires for a well-behaved
        // client. The RPC error shape is what the client sees on a bad
        // call; pin the boundary.
        var test = new TestDb();
        var u = new PortfolioApi.Models.User
        {
            Username = "alice", Email = "a@x",
            PasswordHash = new byte[] { 1 }, PasswordSalt = new byte[] { 1 }
        };
        test.Db.Users.Add(u);
        await test.Db.SaveChangesAsync();
        var sut = new AccountMethods(new SpyAccountService(), test.Db);

        var act = async () => await sut.SetBio(
            new SetBioParams { Bio = new string('a', 281) },
            TestRpcContext.User(u.Id));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*280*");
    }

    [Fact]
    public async Task SetBio_requires_a_signed_in_caller()
    {
        var sut = new AccountMethods(new SpyAccountService(), new TestDb().Db);

        var act = async () => await sut.SetBio(new SetBioParams { Bio = "x" }, TestRpcContext.Anonymous());

        await act.Should().ThrowAsync<AuthFailedException>();
    }

    private sealed class SpyAccountService : IAccountService
    {
        public CommentDeletionStrategy? LastStrategy { get; private set; }

        public Task<AccountExport> ExportAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AccountExport(
                userId, "u", "u@x", true, null, 24, false, false, 0, DateTime.UtcNow, NotifyOnComment: true,
                Bio: "",
                Array.Empty<AccountExportPost>(),
                Array.Empty<AccountExportComment>(),
                Array.Empty<AccountExportRefreshToken>(),
                Array.Empty<AccountExportAuditEvent>(),
                SharedLocation: null,
                Bookmarks: Array.Empty<AccountExportBookmark>()));

        public Task DeleteAsync(Guid userId, CommentDeletionStrategy commentStrategy, CancellationToken cancellationToken = default)
        {
            LastStrategy = commentStrategy;
            return Task.CompletedTask;
        }
    }
}
