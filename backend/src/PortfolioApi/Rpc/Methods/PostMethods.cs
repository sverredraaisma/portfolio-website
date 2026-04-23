using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PortfolioApi.Data;
using PortfolioApi.Models;
using PortfolioApi.Services;

namespace PortfolioApi.Rpc.Methods;

// ---- Param records ---------------------------------------------------------

public sealed record PostListParams;

public sealed record GetPostParams
{
    public required string Slug { get; init; }
}

public sealed record CreatePostParams
{
    public required string Title { get; init; }
    public required string Slug { get; init; }
    public required JsonElement Blocks { get; init; }
    public bool Published { get; init; } = false;
}

public sealed record UpdatePostParams
{
    public required Guid Id { get; init; }
    public string? Title { get; init; }
    public string? Slug { get; init; }
    public JsonElement? Blocks { get; init; }
    public bool? Published { get; init; }
}

public sealed record DeletePostParams
{
    public required Guid Id { get; init; }
}

public sealed record UploadImageParams
{
    public required string DataBase64 { get; init; }
}

// ---- Response records ------------------------------------------------------

public sealed record PostSummary(Guid Id, string Title, string Slug, DateTime CreatedAt, string Author);

public sealed record PostDetail(
    Guid Id,
    string Title,
    string Slug,
    JsonElement Blocks,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    bool Published,
    string Author);

public sealed record CreatePostResult(Guid Id, string Slug);

public sealed record ImageUploadResult(string Url);

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

    public async Task<List<PostSummary>> List(PostListParams _, RpcContext __)
    {
        return await _db.Posts
            .Where(p => p.Published)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new PostSummary(p.Id, p.Title, p.Slug, p.CreatedAt, p.Author!.Username))
            .ToListAsync();
    }

    public async Task<PostDetail> Get(GetPostParams p, RpcContext ctx)
    {
        var post = await _db.Posts
            .Include(x => x.Author)
            .FirstOrDefaultAsync(x => x.Slug == p.Slug)
            ?? throw new InvalidOperationException("Post not found");

        // Drafts are only visible to their author. Anyone else gets a not-found
        // response — same shape as a non-existent slug, no enumeration.
        if (!post.Published && post.AuthorId != ctx.UserId)
            throw new InvalidOperationException("Post not found");

        return new PostDetail(
            post.Id,
            post.Title,
            post.Slug,
            JsonDocument.Parse(post.Blocks.RootElement.GetRawText()).RootElement,
            post.CreatedAt,
            post.UpdatedAt,
            post.Published,
            post.Author!.Username);
    }

    public async Task<CreatePostResult> Create(CreatePostParams p, RpcContext ctx)
    {
        var userId = ctx.RequireUserId();

        var blocksRaw = p.Blocks.GetRawText();
        if (blocksRaw.Length > MaxBlocksDocBytes)
            throw new InvalidOperationException($"blocks document exceeds {MaxBlocksDocBytes} bytes");

        var post = new Post
        {
            Title = NonNull(p.Title, "title", 200),
            Slug = NormaliseSlug(NonNull(p.Slug, "slug", 200)),
            Blocks = JsonDocument.Parse(blocksRaw),
            Published = p.Published,
            AuthorId = userId
        };
        _db.Posts.Add(post);
        await _db.SaveChangesAsync();
        return new CreatePostResult(post.Id, post.Slug);
    }

    public async Task<OkResult> Update(UpdatePostParams p, RpcContext ctx)
    {
        var userId = ctx.RequireUserId();

        var post = await _db.Posts.FindAsync(p.Id) ?? throw new InvalidOperationException("Post not found");
        if (post.AuthorId != userId) throw new AuthFailedException("Not your post");

        if (p.Title is not null) post.Title = NonNull(p.Title, "title", 200);
        if (p.Slug is not null) post.Slug = NormaliseSlug(NonNull(p.Slug, "slug", 200));
        if (p.Blocks is { } blocks)
        {
            var raw = blocks.GetRawText();
            if (raw.Length > MaxBlocksDocBytes)
                throw new InvalidOperationException($"blocks document exceeds {MaxBlocksDocBytes} bytes");
            post.Blocks = JsonDocument.Parse(raw);
        }
        if (p.Published.HasValue) post.Published = p.Published.Value;
        post.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return new OkResult();
    }

    public async Task<OkResult> Delete(DeletePostParams p, RpcContext ctx)
    {
        var userId = ctx.RequireUserId();

        var post = await _db.Posts.FindAsync(p.Id) ?? throw new InvalidOperationException("Post not found");
        if (post.AuthorId != userId) throw new AuthFailedException("Not your post");

        _db.Posts.Remove(post);
        await _db.SaveChangesAsync();
        return new OkResult();
    }

    /// Accepts base64-encoded image bytes, converts to WebP, returns the public URL.
    public async Task<ImageUploadResult> UploadImage(UploadImageParams p, RpcContext ctx)
    {
        ctx.RequireUserId();

        if (p.DataBase64.Length > MaxImageBase64Bytes)
            throw new InvalidOperationException("Image too large");

        byte[] bytes;
        try { bytes = Convert.FromBase64String(p.DataBase64); }
        catch (FormatException) { throw new InvalidOperationException("Image data is not valid base64"); }

        if (bytes.Length > MaxImageRawBytes)
            throw new InvalidOperationException("Image too large");

        using var ms = new MemoryStream(bytes);
        var url = await _images.ConvertToWebpAsync(ms);
        return new ImageUploadResult(url);
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
