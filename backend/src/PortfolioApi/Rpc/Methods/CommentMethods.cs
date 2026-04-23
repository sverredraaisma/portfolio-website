using Microsoft.EntityFrameworkCore;
using PortfolioApi.Data;
using PortfolioApi.Models;
using PortfolioApi.Services;

namespace PortfolioApi.Rpc.Methods;

// ---- Param records ---------------------------------------------------------

public sealed record ListCommentsParams
{
    public required Guid PostId { get; init; }
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

public sealed record CommentDto(Guid Id, string Body, DateTime CreatedAt, string Author);

public class CommentMethods
{
    private readonly AppDbContext _db;

    public CommentMethods(AppDbContext db) => _db = db;

    public async Task<List<CommentDto>> List(ListCommentsParams p, RpcContext _)
    {
        return await _db.Comments
            .Where(c => c.PostId == p.PostId)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new CommentDto(c.Id, c.Body, c.CreatedAt, c.Author!.Username))
            .ToListAsync();
    }

    public async Task<CommentDto> Create(CreateCommentParams p, RpcContext ctx)
    {
        var userId = ctx.RequireUserId();

        var body = p.Body.Trim();
        if (body.Length == 0) throw new InvalidOperationException("body required");
        if (body.Length > 2000) throw new InvalidOperationException("body too long");

        if (!await _db.Posts.AnyAsync(x => x.Id == p.PostId))
            throw new InvalidOperationException("Post not found");

        var c = new Comment { PostId = p.PostId, AuthorId = userId, Body = body };
        _db.Comments.Add(c);
        await _db.SaveChangesAsync();

        var author = await _db.Users.Where(u => u.Id == userId).Select(u => u.Username).FirstAsync();
        return new CommentDto(c.Id, c.Body, c.CreatedAt, author);
    }

    public async Task<OkResult> Delete(DeleteCommentParams p, RpcContext ctx)
    {
        var userId = ctx.RequireUserId();

        var c = await _db.Comments.FindAsync(p.Id) ?? throw new InvalidOperationException("Comment not found");
        if (c.AuthorId != userId) throw new AuthFailedException("Not your comment");

        _db.Comments.Remove(c);
        await _db.SaveChangesAsync();
        return new OkResult();
    }
}
