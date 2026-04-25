using Microsoft.EntityFrameworkCore;
using PortfolioApi.Constants;
using PortfolioApi.Data;
using PortfolioApi.Models;

namespace PortfolioApi.Services;

public sealed class LocationService : ILocationService
{
    // Each user picks a precision in this range. 5 ≈ ~1m ("find me in this
    // building"), 0 ≈ ~111km ("somewhere on the continent"). Anything
    // outside is rejected — values < 0 are nonsensical, values > 5 leak
    // sub-metre accuracy that GPS rarely actually achieves and pretending
    // it's meaningful would mislead viewers.
    private const int MinPrecision = 0;
    private const int MaxPrecision = 5;
    private const int DefaultPrecision = 3;

    private readonly AppDbContext _db;
    private readonly IGeocodingService _geocoder;
    private readonly IAuditService _audit;

    public LocationService(AppDbContext db, IGeocodingService geocoder, IAuditService audit)
    {
        _db = db;
        _geocoder = geocoder;
        _audit = audit;
    }

    public async Task SetCoordsAsync(Guid userId, double latitude, double longitude, string? label, int? precisionDecimals, CancellationToken cancellationToken = default)
    {
        ValidateCoords(latitude, longitude);
        var precision = await ValidatePrecisionOrDefault(precisionDecimals, userId, cancellationToken);
        // Defence in depth: the frontend rounds before the RPC call so the
        // server never sees raw GPS, but a misbehaving or rolled-back client
        // can't sneak past that. Re-rounding here means the stored row is
        // never more precise than the user's chosen tier — so an account
        // export, an admin DB peek, or a backup leak only ever reveals the
        // tier the user agreed to publish.
        await UpsertAsync(userId,
            Math.Round(latitude, precision),
            Math.Round(longitude, precision),
            NormaliseLabel(label),
            precision: precision, source: "browser", cancellationToken);
    }

    public async Task SetByNameAsync(Guid userId, string placeName, string? label, int? precisionDecimals, CancellationToken cancellationToken = default)
    {
        var trimmed = (placeName ?? "").Trim();
        if (trimmed.Length == 0) throw new InvalidOperationException("place required");
        if (trimmed.Length > 200) throw new InvalidOperationException("place too long");

        var found = await _geocoder.SearchAsync(trimmed, cancellationToken)
            ?? throw new InvalidOperationException("Could not find that place. Try a more specific query.");

        // User-supplied label wins; geocoder's display name fills in when
        // the user didn't supply one.
        var effectiveLabel = NormaliseLabel(label) ?? NormaliseLabel(found.DisplayName);
        var precision = await ValidatePrecisionOrDefault(precisionDecimals, userId, cancellationToken);

        // Same rounding contract as the browser path — the geocoder may
        // have returned 7-decimal-place coords ("centroid of Amsterdam"),
        // but the user picked, say, "city" precision and shouldn't have
        // anything finer than that persisted on their behalf.
        await UpsertAsync(userId,
            Math.Round(found.Latitude, precision),
            Math.Round(found.Longitude, precision),
            effectiveLabel,
            precision: precision, source: "named", cancellationToken);
    }

    public async Task<bool> UpdateMetaAsync(Guid userId, string? label, int? precisionDecimals, bool clearLabel, CancellationToken cancellationToken = default)
    {
        var existing = await _db.SharedLocations.FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);
        if (existing is null) return false;

        var changed = false;
        if (clearLabel)
        {
            if (existing.Label is not null) { existing.Label = null; changed = true; }
        }
        else if (label is not null)
        {
            var normalised = NormaliseLabel(label);
            if (!string.Equals(existing.Label, normalised, StringComparison.Ordinal))
            {
                existing.Label = normalised;
                changed = true;
            }
        }
        if (precisionDecimals.HasValue)
        {
            ValidatePrecision(precisionDecimals.Value);
            if (existing.PrecisionDecimals != precisionDecimals.Value)
            {
                existing.PrecisionDecimals = precisionDecimals.Value;
                // Re-round the stored coords to the new tier. Without this
                // a user who switched from "exact" to "city" would keep
                // their full-precision lat/lon stored on the row — the
                // public list would still round on output, but a DB peek
                // (or a future export bug) would expose precision the
                // user has explicitly dialled down. Rounding is idempotent
                // when the new tier is finer than what's stored.
                existing.Latitude = Math.Round(existing.Latitude, precisionDecimals.Value);
                existing.Longitude = Math.Round(existing.Longitude, precisionDecimals.Value);
                changed = true;
            }
        }
        if (changed)
        {
            existing.UpdatedAt = DateTime.UtcNow;
            _audit.Record(userId, AuditKind.LocationUpdated, "meta");
            await _db.SaveChangesAsync(cancellationToken);
        }
        return true;
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
                s.PrecisionDecimals,
                s.UpdatedAt
            })
            .ToListAsync(cancellationToken);

        // Per-row precision: each user picks how rounded their coords are.
        // Rounding happens in-memory after the projection so the SELECT
        // stays a plain projection — EF doesn't have to translate Round.
        return rows.Select(r => new SharedLocationDto(
            r.Username,
            r.IsAdmin,
            Math.Round(r.Latitude, r.PrecisionDecimals),
            Math.Round(r.Longitude, r.PrecisionDecimals),
            r.Label,
            r.PrecisionDecimals,
            r.UpdatedAt)).ToList();
    }

    public async Task<SharedLocationDto?> GetMineAsync(Guid userId, CancellationToken cancellationToken = default)
    {
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
                s.PrecisionDecimals,
                s.UpdatedAt
            })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null ? null : new SharedLocationDto(
            row.Username, row.IsAdmin,
            Math.Round(row.Latitude, row.PrecisionDecimals),
            Math.Round(row.Longitude, row.PrecisionDecimals),
            row.Label, row.PrecisionDecimals, row.UpdatedAt);
    }

    private async Task UpsertAsync(Guid userId, double lat, double lon, string? label, int precision, string source, CancellationToken cancellationToken)
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
                Source = source,
                PrecisionDecimals = precision
            });
        }
        else
        {
            existing.Latitude = lat;
            existing.Longitude = lon;
            existing.Label = label;
            existing.Source = source;
            existing.PrecisionDecimals = precision;
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

    private static void ValidatePrecision(int precision)
    {
        if (precision < MinPrecision || precision > MaxPrecision)
            throw new InvalidOperationException($"precisionDecimals must be between {MinPrecision} and {MaxPrecision}");
    }

    /// On a fresh share with no precision argument we want the user's
    /// previously-chosen precision rather than reverting to the default —
    /// otherwise a user who picked "exact" for a meet-up would silently
    /// snap back to the default the next time they re-shared. Falls back
    /// to 3 only when no row exists yet.
    private async Task<int> ValidatePrecisionOrDefault(int? supplied, Guid userId, CancellationToken ct)
    {
        if (supplied.HasValue) { ValidatePrecision(supplied.Value); return supplied.Value; }
        var existing = await _db.SharedLocations
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .Select(s => (int?)s.PrecisionDecimals)
            .FirstOrDefaultAsync(ct);
        return existing ?? DefaultPrecision;
    }

    private static string? NormaliseLabel(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim();
        return trimmed.Length > 120 ? trimmed[..120] : trimmed;
    }
}
