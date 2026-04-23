using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PortfolioApi.Data;
using PortfolioApi.Models;
using PortfolioApi.Services;

namespace PortfolioApi.Rpc.Methods;

public class CommentMethods
{
    private readonly AppDbContext _db;

    public CommentMethods(AppDbContext db) => _db = db;

    public async Task<object?> List(JsonElement? @params, RpcContext _)
    {
        var p = @params ?? throw new InvalidOperationException("params required");
        var postId = Guid.Parse(p.GetProperty("postId").GetString()!);

        return await _db.Comments
            .Where(c => c.PostId == postId)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new
            {
                c.Id,
                c.Body,
                c.CreatedAt,
                author = c.Author!.Username
            })
            .ToListAsync();
    }

    public async Task<object?> Create(JsonElement? @params, RpcContext ctx)
    {
        var userId = ctx.RequireUserId();
        var p = @params ?? throw new InvalidOperationException("params required");

        var postId = Guid.Parse(p.GetProperty("postId").GetString()!);
        var body = (p.GetProperty("body").GetString() ?? "").Trim();
        if (body.Length == 0) throw new InvalidOperationException("body required");
        if (body.Length > 2000) throw new InvalidOperationException("body too long");

        if (!await _db.Posts.AnyAsync(x => x.Id == postId))
            throw new InvalidOperationException("Post not found");

        var c = new Comment { PostId = postId, AuthorId = userId, Body = body };
        _db.Comments.Add(c);
        await _db.SaveChangesAsync();

        var author = await _db.Users.Where(u => u.Id == userId).Select(u => u.Username).FirstAsync();
        return new { c.Id, c.Body, c.CreatedAt, author };
    }

    public async Task<object?> Delete(JsonElement? @params, RpcContext ctx)
    {
        var userId = ctx.RequireUserId();
        var p = @params ?? throw new InvalidOperationException("params required");
        var id = Guid.Parse(p.GetProperty("id").GetString()!);

        var c = await _db.Comments.FindAsync(id) ?? throw new InvalidOperationException("Comment not found");
        if (c.AuthorId != userId) throw new AuthFailedException("Not your comment");

        _db.Comments.Remove(c);
        await _db.SaveChangesAsync();
        return new { ok = true };
    }
}
