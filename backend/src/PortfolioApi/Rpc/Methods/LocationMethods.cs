using PortfolioApi.Services;

namespace PortfolioApi.Rpc.Methods;

// ---- Param records ---------------------------------------------------------

public sealed record ShareCoordsParams
{
    public required double Latitude { get; init; }
    public required double Longitude { get; init; }
    /// Optional human-readable hint ("Software developer meetup"). The
    /// frontend sends what the user typed; the server caps the length and
    /// trims. Null/empty leaves the existing label untouched on a re-share.
    public string? Label { get; init; }
    /// Optional precision in decimal places (0..5). Null inherits the
    /// caller's previous choice if any, falling back to 3 (~110m) for a
    /// brand-new share.
    public int? PrecisionDecimals { get; init; }
}

public sealed record ShareNamedParams
{
    public required string Place { get; init; }
    /// Optional user-supplied label. When non-null overrides the geocoder's
    /// display name for the saved row. Bounded length, trimmed.
    public string? Label { get; init; }
    /// Optional precision in decimal places (0..5). See ShareCoordsParams.
    public int? PrecisionDecimals { get; init; }
}

public sealed record UpdateLocationMetaParams
{
    /// New label. Null keeps the existing label.
    public string? Label { get; init; }
    /// New precision. Null keeps the existing precision.
    public int? PrecisionDecimals { get; init; }
    /// Setting `clearLabel: true` is the only way to remove an existing
    /// label without changing it to a new value — null Label means "leave
    /// alone", which would otherwise have no opt-out path.
    public bool ClearLabel { get; init; }
}

public class LocationMethods
{
    private readonly ILocationService _locations;

    public LocationMethods(ILocationService locations) => _locations = locations;

    /// Public list. Each row is rounded to the per-user precision the
    /// sharer picked.
    public async Task<IReadOnlyList<SharedLocationDto>> List(RpcContext ctx) =>
        await _locations.ListAsync(ctx.CancellationToken);

    /// Authenticated: caller's current row, or null. Lets /account show
    /// "you're sharing X" without filtering the public list client-side.
    public async Task<SharedLocationDto?> GetMine(RpcContext ctx)
    {
        var userId = ctx.RequireUserId();
        return await _locations.GetMineAsync(userId, ctx.CancellationToken);
    }

    public async Task<OkResult> ShareCoords(ShareCoordsParams p, RpcContext ctx)
    {
        var userId = ctx.RequireUserId();
        await _locations.SetCoordsAsync(userId, p.Latitude, p.Longitude, p.Label, p.PrecisionDecimals, ctx.CancellationToken);
        return new OkResult();
    }

    public async Task<OkResult> ShareNamed(ShareNamedParams p, RpcContext ctx)
    {
        var userId = ctx.RequireUserId();
        await _locations.SetByNameAsync(userId, p.Place, p.Label, p.PrecisionDecimals, ctx.CancellationToken);
        return new OkResult();
    }

    /// Update label and/or precision on the caller's existing pin without
    /// re-locating. Throws InvalidOperation if no pin exists yet — the
    /// caller should use ShareCoords/ShareNamed first.
    public async Task<OkResult> UpdateMeta(UpdateLocationMetaParams p, RpcContext ctx)
    {
        var userId = ctx.RequireUserId();
        var ok = await _locations.UpdateMetaAsync(userId, p.Label, p.PrecisionDecimals, p.ClearLabel, ctx.CancellationToken);
        if (!ok) throw new InvalidOperationException("No location is currently being shared.");
        return new OkResult();
    }

    public async Task<OkResult> Clear(RpcContext ctx)
    {
        var userId = ctx.RequireUserId();
        await _locations.ClearAsync(userId, ctx.CancellationToken);
        return new OkResult();
    }
}
