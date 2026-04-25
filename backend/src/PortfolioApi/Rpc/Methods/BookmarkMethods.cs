using Microsoft.EntityFrameworkCore;
using PortfolioApi.Data;
using PortfolioApi.Models;
using PortfolioApi.Services;

namespace PortfolioApi.Rpc.Methods;

// ---- Param records ---------------------------------------------------------

public sealed record ToggleBookmarkParams
{
    public required Guid PostId { get; init; }
}

// ---- Response records ------------------------------------------------------

public sealed record BookmarkDto(
    Guid Id,
    Guid PostId,
    string PostTitle,
    string PostSlug,
    string PostAuthor,
    DateTime SavedAt);

/// Compact result for the toggle endpoint so the client can update its UI
/// without a follow-up list call. `IsBookmarked` reflects the post-toggle
/// state.
public sealed record BookmarkToggleResult(bool IsBookmarked);

public class BookmarkMethods
{
    private readonly AppDbContext _db;

    public BookmarkMethods(AppDbContext db) => _db = db;

    /// Lists the caller's bookmarks newest-first. No paging — the cap on
    /// rows-per-user is implicit (humans don't bookmark thousands of
    /// posts) but if it ever matters, page like CommentMethods.List does.
    public async Task<IReadOnlyList<BookmarkDto>> List(RpcContext ctx)
    {
        var userId = ctx.RequireUserId();

        // Bookmarked drafts vanish from the list — they're not reachable
        // from /posts/<slug> for non-authors anyway, so showing them
        // would render a dead link.
        return await _db.Bookmarks
            .AsNoTracking()
            .Where(b => b.UserId == userId && b.Post!.Published)
            .OrderByDescending(b => b.CreatedAt)
            .Select(b => new BookmarkDto(
                b.Id,
                b.PostId,
                b.Post!.Title,
                b.Post!.Slug,
                b.Post!.Author!.Username,
                b.CreatedAt))
            .ToListAsync(ctx.CancellationToken);
    }

    /// Adds the bookmark if it's not there; removes it if it is. The
    /// composite-unique index makes the add idempotent — but we check
    /// first so the response carries the correct post-toggle state.
    public async Task<BookmarkToggleResult> Toggle(ToggleBookmarkParams p, RpcContext ctx)
    {
        var userId = ctx.RequireUserId();

        if (!await _db.Posts.AnyAsync(x => x.Id == p.PostId, ctx.CancellationToken))
            throw new InvalidOperationException("Post not found");

        var existing = await _db.Bookmarks
            .FirstOrDefaultAsync(b => b.UserId == userId && b.PostId == p.PostId, ctx.CancellationToken);

        if (existing is not null)
        {
            _db.Bookmarks.Remove(existing);
            await _db.SaveChangesAsync(ctx.CancellationToken);
            return new BookmarkToggleResult(IsBookmarked: false);
        }

        _db.Bookmarks.Add(new Bookmark { UserId = userId, PostId = p.PostId });
        try
        {
            await _db.SaveChangesAsync(ctx.CancellationToken);
        }
        catch (DbUpdateException)
        {
            // Concurrent toggle from another tab — the unique index fired.
            // Treat as a successful add, the row exists either way.
        }
        return new BookmarkToggleResult(IsBookmarked: true);
    }

    /// Used by the post-detail page to decide whether to show the filled
    /// or hollow bookmark icon. Returns false for anonymous callers — no
    /// auth requirement, just a cheap "is it saved" probe.
    public async Task<BookmarkToggleResult> IsBookmarked(ToggleBookmarkParams p, RpcContext ctx)
    {
        if (ctx.UserId is not Guid userId) return new BookmarkToggleResult(false);
        var exists = await _db.Bookmarks
            .AsNoTracking()
            .AnyAsync(b => b.UserId == userId && b.PostId == p.PostId, ctx.CancellationToken);
        return new BookmarkToggleResult(exists);
    }
}
