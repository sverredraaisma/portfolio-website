using Microsoft.EntityFrameworkCore;
using PortfolioApi.Data;

namespace PortfolioApi.Services;

public class AccountService : IAccountService
{
    private readonly AppDbContext _db;

    public AccountService(AppDbContext db) => _db = db;

    public async Task<AccountExport> ExportAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new AuthFailedException("Unknown user");

        var posts = await _db.Posts
            .AsNoTracking()
            .Where(p => p.AuthorId == userId)
            .OrderBy(p => p.CreatedAt)
            .Select(p => new AccountExportPost(p.Id, p.Title, p.Slug, p.CreatedAt, p.UpdatedAt, p.Published))
            .ToListAsync(cancellationToken);

        var comments = await _db.Comments
            .AsNoTracking()
            .Where(c => c.AuthorId == userId)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new AccountExportComment(c.Id, c.PostId, c.Body, c.CreatedAt))
            .ToListAsync(cancellationToken);

        // We deliberately omit TokenHash — the raw refresh token is gone from
        // the database, and the hash is credential material, not user data.
        var refreshTokens = await _db.RefreshTokens
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .OrderBy(t => t.CreatedAt)
            .Select(t => new AccountExportRefreshToken(t.Id, t.CreatedAt, t.ExpiresAt, t.RevokedAt))
            .ToListAsync(cancellationToken);

        return new AccountExport(
            user.Id,
            user.Username,
            user.Email,
            user.EmailVerifiedAt is not null,
            user.IsAdmin,
            user.TotpEnabledAt is not null,
            user.CreatedAt,
            posts,
            comments,
            refreshTokens);
    }

    public async Task DeleteAsync(Guid userId, CommentDeletionStrategy commentStrategy, CancellationToken cancellationToken = default)
    {
        // Wrap the whole thing in a transaction so a partial failure (e.g.
        // FK violation from a missed dependency) leaves the account intact.
        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new AuthFailedException("Unknown user");

        // Comments first — handle the user's choice explicitly. Letting EF
        // rely on ON DELETE SET NULL would also work for "anonymise" but we
        // do the UPDATE explicitly so the behaviour matches what the user
        // selected even if the FK action ever changes.
        if (commentStrategy == CommentDeletionStrategy.Delete)
        {
            await _db.Comments
                .Where(c => c.AuthorId == userId)
                .ExecuteDeleteAsync(cancellationToken);
        }
        else
        {
            await _db.Comments
                .Where(c => c.AuthorId == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.AuthorId, (Guid?)null), cancellationToken);
        }

        // Posts authored by the user — hard delete. Comments on those posts
        // cascade away via the Post→Comments relationship.
        await _db.Posts
            .Where(p => p.AuthorId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        // RefreshTokens cascade away when the user is removed, but issue an
        // explicit delete first so the row count is observable in logs.
        await _db.RefreshTokens
            .Where(t => t.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        _db.Users.Remove(user);
        await _db.SaveChangesAsync(cancellationToken);

        await tx.CommitAsync(cancellationToken);
    }
}
