using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PortfolioApi.Configuration;
using PortfolioApi.Constants;
using PortfolioApi.Data;
using PortfolioApi.Models;

namespace PortfolioApi.Services;

public sealed class LocationService : ILocationService
{
    private readonly AppDbContext _db;
    private readonly IGeocodingService _geocoder;
    private readonly IAuditService _audit;
    private readonly LocationOptions _opt;

    public LocationService(AppDbContext db, IGeocodingService geocoder, IAuditService audit, IOptions<LocationOptions> opt)
    {
        _db = db;
        _geocoder = geocoder;
        _audit = audit;
        _opt = opt.Value;
    }

    public async Task SetCoordsAsync(Guid userId, double latitude, double longitude, string? label, CancellationToken cancellationToken = default)
    {
        ValidateCoords(latitude, longitude);
        await UpsertAsync(userId, latitude, longitude, NormaliseLabel(label), source: "browser", cancellationToken);
    }

    public async Task SetByNameAsync(Guid userId, string placeName, CancellationToken cancellationToken = default)
    {
        var trimmed = (placeName ?? "").Trim();
        if (trimmed.Length == 0) throw new InvalidOperationException("place required");
        if (trimmed.Length > 200) throw new InvalidOperationException("place too long");

        var found = await _geocoder.SearchAsync(trimmed, cancellationToken)
            ?? throw new InvalidOperationException("Could not find that place. Try a more specific query.");

        await UpsertAsync(userId, found.Latitude, found.Longitude,
            NormaliseLabel(found.DisplayName), source: "named", cancellationToken);
    }

    public async Task ClearAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var deleted = await _db.SharedLocations
            .Where(s => s.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        if (deleted > 0)
        {
            _audit.Record(userId, AuditKind.LocationCleared);
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<SharedLocationDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var precision = _opt.PublicPrecisionDecimals;
        var rows = await _db.SharedLocations
            .AsNoTracking()
            .OrderByDescending(s => s.UpdatedAt)
            .Select(s => new
            {
                Username = s.User!.Username,
                IsAdmin = s.User!.IsAdmin,
                s.Latitude,
                s.Longitude,
                s.Label,
                s.UpdatedAt
            })
            .ToListAsync(cancellationToken);

        // Rounding happens in-memory: Math.Round on the projection lets EF
        // ship plain SELECTs and keeps the precision policy in one place.
        return rows.Select(r => new SharedLocationDto(
            r.Username,
            r.IsAdmin,
            Math.Round(r.Latitude, precision),
            Math.Round(r.Longitude, precision),
            r.Label,
            r.UpdatedAt)).ToList();
    }

    public async Task<SharedLocationDto?> GetMineAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var precision = _opt.PublicPrecisionDecimals;
        var row = await _db.SharedLocations
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .Select(s => new
            {
                Username = s.User!.Username,
                IsAdmin = s.User!.IsAdmin,
                s.Latitude,
                s.Longitude,
                s.Label,
                s.UpdatedAt
            })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null ? null : new SharedLocationDto(
            row.Username, row.IsAdmin,
            Math.Round(row.Latitude, precision),
            Math.Round(row.Longitude, precision),
            row.Label, row.UpdatedAt);
    }

    private async Task UpsertAsync(Guid userId, double lat, double lon, string? label, string source, CancellationToken cancellationToken)
    {
        var existing = await _db.SharedLocations.FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);
        var isFirstShare = existing is null;
        if (existing is null)
        {
            _db.SharedLocations.Add(new SharedLocation
            {
                UserId = userId,
                Latitude = lat,
                Longitude = lon,
                Label = label,
                Source = source
            });
        }
        else
        {
            existing.Latitude = lat;
            existing.Longitude = lon;
            existing.Label = label;
            existing.Source = source;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        _audit.Record(userId, isFirstShare ? AuditKind.LocationShared : AuditKind.LocationUpdated, source);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static void ValidateCoords(double lat, double lon)
    {
        if (double.IsNaN(lat) || double.IsNaN(lon)) throw new InvalidOperationException("coords must be real numbers");
        if (lat < -90 || lat > 90)   throw new InvalidOperationException("latitude must be between -90 and 90");
        if (lon < -180 || lon > 180) throw new InvalidOperationException("longitude must be between -180 and 180");
    }

    private static string? NormaliseLabel(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim();
        return trimmed.Length > 120 ? trimmed[..120] : trimmed;
    }
}
