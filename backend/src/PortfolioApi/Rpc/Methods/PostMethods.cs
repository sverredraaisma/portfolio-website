using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PortfolioApi.Data;
using PortfolioApi.Models;
using PortfolioApi.Services;

namespace PortfolioApi.Rpc.Methods;

public class PostMethods
{
    private readonly AppDbContext _db;
    private readonly IImageService _images;

    public PostMethods(AppDbContext db, IImageService images)
    {
        _db = db;
        _images = images;
    }

    public async Task<object?> List(JsonElement? _, RpcContext __)
    {
        var posts = await _db.Posts
            .Where(p => p.Published)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new { p.Id, p.Title, p.Slug, p.CreatedAt, author = p.Author!.Username })
            .ToListAsync();
        return posts;
    }

    public async Task<object?> Get(JsonElement? @params, RpcContext _)
    {
        var p = @params ?? throw new InvalidOperationException("params required");
        var slug = p.GetProperty("slug").GetString() ?? throw new InvalidOperationException("slug required");

        var post = await _db.Posts
            .Include(x => x.Author)
            .FirstOrDefaultAsync(x => x.Slug == slug)
            ?? throw new InvalidOperationException("Post not found");

        return new
        {
            post.Id,
            post.Title,
            post.Slug,
            blocks = JsonDocument.Parse(post.Blocks.RootElement.GetRawText()).RootElement,
            post.CreatedAt,
            post.UpdatedAt,
            author = post.Author!.Username
        };
    }

    public async Task<object?> Create(JsonElement? @params, RpcContext ctx)
    {
        var userId = ctx.RequireUserId();
        var p = @params ?? throw new InvalidOperationException("params required");

        var post = new Post
        {
            Title = p.GetProperty("title").GetString() ?? throw new InvalidOperationException("title required"),
            Slug = p.GetProperty("slug").GetString() ?? throw new InvalidOperationException("slug required"),
            Blocks = JsonDocument.Parse(p.GetProperty("blocks").GetRawText()),
            Published = p.TryGetProperty("published", out var pub) && pub.GetBoolean(),
            AuthorId = userId
        };
        _db.Posts.Add(post);
        await _db.SaveChangesAsync();
        return new { post.Id, post.Slug };
    }

    public async Task<object?> Update(JsonElement? @params, RpcContext ctx)
    {
        var userId = ctx.RequireUserId();
        var p = @params ?? throw new InvalidOperationException("params required");
        var id = Guid.Parse(p.GetProperty("id").GetString()!);

        var post = await _db.Posts.FindAsync(id) ?? throw new InvalidOperationException("Post not found");
        if (post.AuthorId != userId) throw new UnauthorizedAccessException("Not your post");

        if (p.TryGetProperty("title", out var t)) post.Title = t.GetString()!;
        if (p.TryGetProperty("slug", out var s)) post.Slug = s.GetString()!;
        if (p.TryGetProperty("blocks", out var b)) post.Blocks = JsonDocument.Parse(b.GetRawText());
        if (p.TryGetProperty("published", out var pub)) post.Published = pub.GetBoolean();
        post.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return new { ok = true };
    }

    public async Task<object?> Delete(JsonElement? @params, RpcContext ctx)
    {
        var userId = ctx.RequireUserId();
        var p = @params ?? throw new InvalidOperationException("params required");
        var id = Guid.Parse(p.GetProperty("id").GetString()!);

        var post = await _db.Posts.FindAsync(id) ?? throw new InvalidOperationException("Post not found");
        if (post.AuthorId != userId) throw new UnauthorizedAccessException("Not your post");

        _db.Posts.Remove(post);
        await _db.SaveChangesAsync();
        return new { ok = true };
    }

    /// Accepts base64-encoded image bytes, converts to WebP, returns the public URL.
    public async Task<object?> UploadImage(JsonElement? @params, RpcContext ctx)
    {
        ctx.RequireUserId();
        var p = @params ?? throw new InvalidOperationException("params required");
        var b64 = p.GetProperty("dataBase64").GetString() ?? throw new InvalidOperationException("dataBase64 required");

        var bytes = Convert.FromBase64String(b64);
        using var ms = new MemoryStream(bytes);
        var url = await _images.ConvertToWebpAsync(ms);
        return new { url };
    }
}
