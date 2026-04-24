namespace PortfolioApi.Services;

public enum CommentDeletionStrategy
{
    /// Set Comment.AuthorId to NULL — the body and timestamp survive but the
    /// link to the deleted account is broken. Renders as "anonymous".
    Anonymise,
    /// Hard-delete every comment authored by the user.
    Delete
}

public sealed record AccountExportComment(Guid Id, Guid PostId, string Body, DateTime CreatedAt);
public sealed record AccountExportPost(Guid Id, string Title, string Slug, DateTime CreatedAt, DateTime UpdatedAt, bool Published);
public sealed record AccountExportRefreshToken(Guid Id, DateTime CreatedAt, DateTime ExpiresAt, DateTime? RevokedAt);
public sealed record AccountExportAuditEvent(Guid Id, string Kind, string? Detail, DateTime At);

/// Everything the service knows about the account, in a shape suitable for
/// JSON download. No password hashes, no salts, no raw refresh tokens — those
/// are not "the user's data" in the AVG sense, they are credential material.
public sealed record AccountExport(
    Guid Id,
    string Username,
    string Email,
    bool EmailVerified,
    bool IsAdmin,
    bool TotpEnabled,
    int RecoveryCodesRemaining,
    DateTime CreatedAt,
    IReadOnlyList<AccountExportPost> Posts,
    IReadOnlyList<AccountExportComment> Comments,
    IReadOnlyList<AccountExportRefreshToken> RefreshTokens,
    IReadOnlyList<AccountExportAuditEvent> AuditEvents);

public interface IAccountService
{
    /// AVG art. 15 / 20: a user can fetch all data tied to their account in a
    /// machine-readable form.
    Task<AccountExport> ExportAsync(Guid userId, CancellationToken cancellationToken = default);

    /// AVG art. 17: a user can delete their account and all data tied to it.
    /// <paramref name="commentStrategy"/> chooses whether comments are hard-
    /// deleted or anonymised (AuthorId set to NULL). All posts authored by
    /// the user are deleted unconditionally — drafts and published alike —
    /// since posts are first-party content. Refresh tokens cascade-delete.
    Task DeleteAsync(Guid userId, CommentDeletionStrategy commentStrategy, CancellationToken cancellationToken = default);
}
