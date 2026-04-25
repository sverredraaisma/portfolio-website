using PortfolioApi.Services;

namespace PortfolioApi.Rpc.Methods;

// ---- Param records ---------------------------------------------------------

public sealed record ShareCoordsParams
{
    public required double Latitude { get; init; }
    public required double Longitude { get; init; }
    /// Optional human-readable hint ("Amsterdam"). The frontend sends what
    /// the user typed; the server caps the length and trims.
    public string? Label { get; init; }
}

public sealed record ShareNamedParams
{
    public required string Place { get; init; }
}

public class LocationMethods
{
    private readonly ILocationService _locations;

    public LocationMethods(ILocationService locations) => _locations = locations;

    /// Public list. Returns rows already rounded to the configured public
    /// precision so a client snooping the wire doesn't see exact coords.
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
        await _locations.SetCoordsAsync(userId, p.Latitude, p.Longitude, p.Label, ctx.CancellationToken);
        return new OkResult();
    }

    public async Task<OkResult> ShareNamed(ShareNamedParams p, RpcContext ctx)
    {
        var userId = ctx.RequireUserId();
        await _locations.SetByNameAsync(userId, p.Place, ctx.CancellationToken);
        return new OkResult();
    }

    public async Task<OkResult> Clear(RpcContext ctx)
    {
        var userId = ctx.RequireUserId();
        await _locations.ClearAsync(userId, ctx.CancellationToken);
        return new OkResult();
    }
}
