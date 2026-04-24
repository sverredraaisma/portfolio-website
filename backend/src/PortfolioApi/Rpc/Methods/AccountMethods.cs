using PortfolioApi.Services;

namespace PortfolioApi.Rpc.Methods;

// ---- Param records ---------------------------------------------------------

public sealed record DeleteAccountParams
{
    /// "anonymise" | "delete" — controls what happens to the user's comments.
    /// Defaults to anonymise (the less destructive option).
    public string CommentStrategy { get; init; } = "anonymise";
}

public class AccountMethods
{
    private readonly IAccountService _accounts;

    public AccountMethods(IAccountService accounts) => _accounts = accounts;

    /// AVG art. 15 / 20: data subject access + portability. Returns
    /// everything the service knows about the caller's account.
    public Task<AccountExport> Export(RpcContext ctx)
    {
        var userId = ctx.RequireUserId();
        return _accounts.ExportAsync(userId, ctx.CancellationToken);
    }

    /// AVG art. 17: right to erasure. Deletes the account and all data
    /// tied to it; comments are anonymised or deleted per the caller's choice.
    public async Task<OkResult> Delete(DeleteAccountParams p, RpcContext ctx)
    {
        var userId = ctx.RequireUserId();
        var strategy = ParseStrategy(p.CommentStrategy);
        await _accounts.DeleteAsync(userId, strategy, ctx.CancellationToken);
        return new OkResult();
    }

    private static CommentDeletionStrategy ParseStrategy(string raw) => raw?.ToLowerInvariant() switch
    {
        "delete" => CommentDeletionStrategy.Delete,
        // British and American spellings both accepted — defensive against
        // typos given how irreversible the action is.
        "anonymise" or "anonymize" or "" or null => CommentDeletionStrategy.Anonymise,
        _ => throw new InvalidOperationException("commentStrategy must be 'anonymise' or 'delete'")
    };
}
