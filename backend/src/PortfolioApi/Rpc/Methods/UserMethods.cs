using Microsoft.EntityFrameworkCore;
using PortfolioApi.Data;
using PortfolioApi.Services;

namespace PortfolioApi.Rpc.Methods;

// ---- Param records ---------------------------------------------------------

public sealed record GetProfileParams
{
    public required string Username { get; init; }
}

// ---- Response records ------------------------------------------------------

/// One row in the recent-comments strip on a profile. PostSlug + PostTitle
/// give the client enough to link back without a follow-up query.
public sealed record ProfileCommentDto(
    Guid Id,
    string Body,
    DateTime CreatedAt,
    Guid PostId,
    string PostTitle,
    string PostSlug);

/// One row in the recent-posts strip. Drafts are excluded — the profile is
/// public, so only published work shows up here.
public sealed record ProfilePostDto(Guid Id, string Title, string Slug, DateTime CreatedAt);

public sealed record UserProfileDto(
    string Username,
    bool IsAdmin,
    DateTime CreatedAt,
    int PostCount,
    int CommentCount,
    IReadOnlyList<ProfilePostDto> RecentPosts,
    IReadOnlyList<ProfileCommentDto> RecentComments);

public class UserMethods
{
    /// How many rows to surface in each "recent" strip. Small on purpose —
    /// the profile is a snapshot, not a full archive (the post list and
    /// comment moderation pages cover the bulk views).
    private const int RecentLimit = 5;

    private readonly AppDbContext _db;

    public UserMethods(AppDbContext db)
    {
        _db = db;
    }

    public async Task<UserProfileDto> GetProfile(GetProfileParams p, RpcContext ctx)
    {
        // Permissive lookup: /u/Alice or /u/ALICE find the canonical "alice".
        // A value that can't be a valid username at all (whitespace, bad
        // chars, wrong length) yields null and we surface a not-found
        // without bothering the database.
        var key = UsernameNormalizer.NormaliseForLookup(p.Username)
            ?? throw new InvalidOperationException("User not found");

        var user = await _db.Users
            .AsNoTracking()
            .Where(u => u.Username == key)
            .Select(u => new { u.Id, u.Username, u.IsAdmin, u.CreatedAt })
            .FirstOrDefaultAsync(ctx.CancellationToken)
            ?? throw new InvalidOperationException("User not found");

        // Counts: only published posts contribute. A draft is invisible
        // to the public, so it shouldn't bump the public total either.
        var postCount = await _db.Posts
            .AsNoTracking()
            .CountAsync(x => x.AuthorId == user.Id && x.Published, ctx.CancellationToken);

        var commentCount = await _db.Comments
            .AsNoTracking()
            .CountAsync(c => c.AuthorId == user.Id, ctx.CancellationToken);

        var recentPosts = await _db.Posts
            .AsNoTracking()
            .Where(x => x.AuthorId == user.Id && x.Published)
            .OrderByDescending(x => x.CreatedAt)
            .Take(RecentLimit)
            .Select(x => new ProfilePostDto(x.Id, x.Title, x.Slug, x.CreatedAt))
            .ToListAsync(ctx.CancellationToken);

        // Comments on drafts are unreachable from the public site, so
        // exclude them from the strip — clicking the link would 404.
        var recentComments = await _db.Comments
            .AsNoTracking()
            .Where(c => c.AuthorId == user.Id && c.Post!.Published)
            .OrderByDescending(c => c.CreatedAt)
            .Take(RecentLimit)
            .Select(c => new ProfileCommentDto(
                c.Id, c.Body, c.CreatedAt,
                c.PostId, c.Post!.Title, c.Post!.Slug))
            .ToListAsync(ctx.CancellationToken);

        return new UserProfileDto(
            user.Username,
            user.IsAdmin,
            user.CreatedAt,
            postCount,
            commentCount,
            recentPosts,
            recentComments);
    }
}
