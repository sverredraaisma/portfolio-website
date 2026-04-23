using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PortfolioApi.Data;
using PortfolioApi.Models;
using PortfolioApi.Services;

namespace PortfolioApi.Rpc.Methods;

public class PostMethods
{
    // Hard limits to keep request bodies bounded. The Kestrel-level cap covers
    // the whole RPC body; these protect specific fields.
    private const int MaxBlocksDocBytes = 256 * 1024;       // 256 KiB of JSON
    private const int MaxImageBase64Bytes = 8 * 1024 * 1024; // 8 MiB encoded
    private const int MaxImageRawBytes = 6 * 1024 * 1024;    // ~6 MiB decoded

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

    public async Task<object?> Get(JsonElement? @params, RpcContext ctx)
    {
        var p = @params ?? throw new InvalidOperationException("params required");
        var slug = p.GetProperty("slug").GetString() ?? throw new InvalidOperationException("slug required");

        var post = await _db.Posts
            .Include(x => x.Author)
            .FirstOrDefaultAsync(x => x.Slug == slug)
            ?? throw new InvalidOperationException("Post not found");

        // Drafts are only visible to their author. Anyone else gets a not-found
        // response — same shape as a non-existent slug, no enumeration.
        if (!post.Published && post.AuthorId != ctx.UserId)
            throw new InvalidOperationException("Post not found");

        return new
        {
            post.Id,
            post.Title,
            post.Slug,
            blocks = JsonDocument.Parse(post.Blocks.RootElement.GetRawText()).RootElement,
            post.CreatedAt,
            post.UpdatedAt,
            published = post.Published,
            author = post.Author!.Username
        };
    }

    public async Task<object?> Create(JsonElement? @params, RpcContext ctx)
    {
        var userId = ctx.RequireUserId();
        var p = @params ?? throw new InvalidOperationException("params required");

        var blocksRaw = p.GetProperty("blocks").GetRawText();
        if (blocksRaw.Length > MaxBlocksDocBytes)
            throw new InvalidOperationException($"blocks document exceeds {MaxBlocksDocBytes} bytes");

        var post = new Post
        {
            Title = RequireString(p, "title", maxLen: 200),
            Slug = NormaliseSlug(RequireString(p, "slug", maxLen: 200)),
            Blocks = JsonDocument.Parse(blocksRaw),
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
        if (post.AuthorId != userId) throw new AuthFailedException("Not your post");

        if (p.TryGetProperty("title", out var t)) post.Title = NonNull(t.GetString(), "title", 200);
        if (p.TryGetProperty("slug", out var s)) post.Slug = NormaliseSlug(NonNull(s.GetString(), "slug", 200));
        if (p.TryGetProperty("blocks", out var b))
        {
            var raw = b.GetRawText();
            if (raw.Length > MaxBlocksDocBytes)
                throw new InvalidOperationException($"blocks document exceeds {MaxBlocksDocBytes} bytes");
            post.Blocks = JsonDocument.Parse(raw);
        }
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
        if (post.AuthorId != userId) throw new AuthFailedException("Not your post");

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

        if (b64.Length > MaxImageBase64Bytes)
            throw new InvalidOperationException("Image too large");

        byte[] bytes;
        try { bytes = Convert.FromBase64String(b64); }
        catch (FormatException) { throw new InvalidOperationException("Image data is not valid base64"); }

        if (bytes.Length > MaxImageRawBytes)
            throw new InvalidOperationException("Image too large");

        using var ms = new MemoryStream(bytes);
        var url = await _images.ConvertToWebpAsync(ms);
        return new { url };
    }

    private static string RequireString(JsonElement p, string name, int maxLen)
    {
        var v = p.GetProperty(name).GetString() ?? throw new InvalidOperationException($"{name} required");
        return NonNull(v, name, maxLen);
    }

    private static string NonNull(string? v, string name, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(v)) throw new InvalidOperationException($"{name} required");
        if (v.Length > maxLen) throw new InvalidOperationException($"{name} too long (max {maxLen})");
        return v;
    }

    // Slugs feed the URL — keep them to a safe character set.
    private static string NormaliseSlug(string s)
    {
        var slug = new string(s.ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '-')
            .ToArray())
            .Trim('-');
        if (slug.Length == 0) throw new InvalidOperationException("slug must contain letters or digits");
        // Avoid colliding with the file-routed /posts/new editor.
        if (slug == "new") throw new InvalidOperationException("slug 'new' is reserved");
        return slug;
    }
}
