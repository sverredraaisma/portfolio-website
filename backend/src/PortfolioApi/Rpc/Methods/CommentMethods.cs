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

public sealed record CreateCommentParams
{
    public required Guid PostId { get; init; }
    public required string Body { get; init; }
}

public sealed record DeleteCommentParams
{
    public required Guid Id { get; init; }
}

// ---- Response records ------------------------------------------------------

public sealed record CommentDto(Guid Id, string Body, DateTime CreatedAt, string Author, bool AuthorIsAdmin);

public class CommentMethods
{
    private readonly AppDbContext _db;

    public CommentMethods(AppDbContext db) => _db = db;

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
            .Select(c => new CommentDto(c.Id, c.Body, c.CreatedAt, c.Author!.Username, c.Author!.IsAdmin))
            .ToListAsync(ctx.CancellationToken);

        var hasMore = rows.Count > pageSize;
        if (hasMore) rows.RemoveAt(rows.Count - 1);

        return new PaginatedResult<CommentDto>(rows, page, pageSize, hasMore);
    }

    public async Task<CommentDto> Create(CreateCommentParams p, RpcContext ctx)
    {
        var userId = ctx.RequireUserId();

        var body = p.Body.Trim();
        if (body.Length == 0) throw new InvalidOperationException("body required");
        if (body.Length > 2000) throw new InvalidOperationException("body too long");

        if (!await _db.Posts.AnyAsync(x => x.Id == p.PostId, ctx.CancellationToken))
            throw new InvalidOperationException("Post not found");

        var c = new Comment { PostId = p.PostId, AuthorId = userId, Body = body };
        _db.Comments.Add(c);
        await _db.SaveChangesAsync(ctx.CancellationToken);

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
        if (c.AuthorId != userId) throw new AuthFailedException("Not your comment");

        _db.Comments.Remove(c);
        await _db.SaveChangesAsync(ctx.CancellationToken);
        return new OkResult();
    }
}
