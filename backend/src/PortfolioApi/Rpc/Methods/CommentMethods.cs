using Microsoft.EntityFrameworkCore;
using PortfolioApi.Data;
using PortfolioApi.Models;
using PortfolioApi.Services;

namespace PortfolioApi.Rpc.Methods;

// ---- Param records ---------------------------------------------------------

public sealed record ListCommentsParams
{
    public required Guid PostId { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}

public sealed record ListAllCommentsParams
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}

public sealed record CreateCommentParams
{
    public required Guid PostId { get; init; }
    public required string Body { get; init; }
}

public sealed record DeleteCommentParams
{
    public required Guid Id { get; init; }
}

public sealed record UpdateCommentParams
{
    public required Guid Id { get; init; }
    public required string Body { get; init; }
}

// ---- Response records ------------------------------------------------------

public sealed record CommentDto(Guid Id, string Body, DateTime CreatedAt, string Author, bool AuthorIsAdmin);

/// Moderation view: includes the host post so the moderator can jump to it.
public sealed record CommentModerationDto(
    Guid Id,
    string Body,
    DateTime CreatedAt,
    string Author,
    bool AuthorIsAdmin,
    Guid PostId,
    string PostTitle,
    string PostSlug);

public class CommentMethods
{
    private readonly AppDbContext _db;
    private readonly ICommentThrottle _throttle;

    public CommentMethods(AppDbContext db, ICommentThrottle throttle)
    {
        _db = db;
        _throttle = throttle;
    }

    public async Task<PaginatedResult<CommentDto>> List(ListCommentsParams p, RpcContext ctx)
    {
        var page = p.Page < 1 ? 1 : p.Page;
        var pageSize = Math.Clamp(p.PageSize, 1, 200);

        var rows = await _db.Comments
            .AsNoTracking()
            .Where(c => c.PostId == p.PostId)
            .OrderBy(c => c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize + 1)
            // Author is null when the user deleted their account and chose
            // "anonymise" — render as "anonymous" with no admin marker.
            .Select(c => new CommentDto(
                c.Id,
                c.Body,
                c.CreatedAt,
                c.Author == null ? "anonymous" : c.Author.Username,
                c.Author != null && c.Author.IsAdmin))
            .ToListAsync(ctx.CancellationToken);

        var hasMore = rows.Count > pageSize;
        if (hasMore) rows.RemoveAt(rows.Count - 1);

        return new PaginatedResult<CommentDto>(rows, page, pageSize, hasMore);
    }

    public async Task<CommentDto> Create(CreateCommentParams p, RpcContext ctx)
    {
        var userId = ctx.RequireUserId();
        // Per-user sliding-window cap. Same shape as the login throttle —
        // throws AuthFailedException so the router maps to 401 with the
        // generic "Not authorized" wire message; the specific reason stays
        // in server logs.
        _throttle.EnsureCanComment(userId);

        var body = p.Body.Trim();
        if (body.Length == 0) throw new InvalidOperationException("body required");
        if (body.Length > 2000) throw new InvalidOperationException("body too long");

        if (!await _db.Posts.AnyAsync(x => x.Id == p.PostId, ctx.CancellationToken))
            throw new InvalidOperationException("Post not found");

        var c = new Comment { PostId = p.PostId, AuthorId = userId, Body = body };
        _db.Comments.Add(c);
        await _db.SaveChangesAsync(ctx.CancellationToken);
        _throttle.Record(userId);

        var author = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Username, u.IsAdmin })
            .FirstAsync(ctx.CancellationToken);
        return new CommentDto(c.Id, c.Body, c.CreatedAt, author.Username, author.IsAdmin);
    }

    public async Task<OkResult> Delete(DeleteCommentParams p, RpcContext ctx)
    {
        var userId = ctx.RequireUserId();

        var c = await _db.Comments.FindAsync(new object?[] { p.Id }, ctx.CancellationToken)
            ?? throw new InvalidOperationException("Comment not found");

        // Authors can delete their own; admins can delete any (moderation).
        if (c.AuthorId != userId && !ctx.IsAdmin)
            throw new AuthFailedException("Not your comment");

        _db.Comments.Remove(c);
        await _db.SaveChangesAsync(ctx.CancellationToken);
        return new OkResult();
    }

    public async Task<PaginatedResult<CommentModerationDto>> ListAll(ListAllCommentsParams p, RpcContext ctx)
    {
        // Admin-only: the moderation queue should not enumerate user-supplied
        // content for non-admin callers.
        ctx.RequireAdmin();

        var page = p.Page < 1 ? 1 : p.Page;
        var pageSize = Math.Clamp(p.PageSize, 1, 200);

        var rows = await _db.Comments
            .AsNoTracking()
            .OrderByDescending(c => c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize + 1)
            .Select(c => new CommentModerationDto(
                c.Id,
                c.Body,
                c.CreatedAt,
                c.Author == null ? "anonymous" : c.Author.Username,
                c.Author != null && c.Author.IsAdmin,
                c.PostId,
                c.Post!.Title,
                c.Post!.Slug))
            .ToListAsync(ctx.CancellationToken);

        var hasMore = rows.Count > pageSize;
        if (hasMore) rows.RemoveAt(rows.Count - 1);

        return new PaginatedResult<CommentModerationDto>(rows, page, pageSize, hasMore);
    }

    public async Task<CommentDto> Update(UpdateCommentParams p, RpcContext ctx)
    {
        var userId = ctx.RequireUserId();

        var body = p.Body.Trim();
        if (body.Length == 0) throw new InvalidOperationException("body required");
        if (body.Length > 2000) throw new InvalidOperationException("body too long");

        var c = await _db.Comments
            .Include(x => x.Author)
            .FirstOrDefaultAsync(x => x.Id == p.Id, ctx.CancellationToken)
            ?? throw new InvalidOperationException("Comment not found");

        // Edit is author-only on purpose: an admin shouldn't put words in
        // someone else's mouth. Admins can still moderate by deleting.
        if (c.AuthorId != userId)
            throw new AuthFailedException("Not your comment");

        c.Body = body;
        await _db.SaveChangesAsync(ctx.CancellationToken);

        return new CommentDto(
            c.Id,
            c.Body,
            c.CreatedAt,
            c.Author == null ? "anonymous" : c.Author.Username,
            c.Author != null && c.Author.IsAdmin);
    }
}
