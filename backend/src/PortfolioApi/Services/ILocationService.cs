namespace PortfolioApi.Services;

/// Public-facing list row. Coords are already rounded to the row's
/// configured precision so callers don't need to do it themselves.
/// PrecisionDecimals is included so the UI can show "this user is
/// sharing at city level" / "exact" without recomputing it.
public sealed record SharedLocationDto(
    string Username,
    bool IsAdmin,
    double Latitude,
    double Longitude,
    string? Label,
    int PrecisionDecimals,
    DateTime UpdatedAt);

public interface ILocationService
{
    /// Set or update the caller's shared location with raw coords. Validates
    /// the supplied lat/lon ranges and the precision choice; throws
    /// InvalidOperationException on bad input.
    Task SetCoordsAsync(Guid userId, double latitude, double longitude, string? label, int? precisionDecimals, CancellationToken cancellationToken = default);

    /// Geocode a place name via the IGeocodingService and store the result.
    /// When the user supplies a label it overrides the geocoder's display
    /// name; otherwise the display name fills it in.
    Task SetByNameAsync(Guid userId, string placeName, string? label, int? precisionDecimals, CancellationToken cancellationToken = default);

    /// Update label and/or precision on the caller's existing row without
    /// touching coords or Source. No-op (returns false) when no row exists.
    /// Either argument may be null to leave that field untouched.
    Task<bool> UpdateMetaAsync(Guid userId, string? label, int? precisionDecimals, bool clearLabel, CancellationToken cancellationToken = default);

    /// Removes the caller's row. No-op if no row exists.
    Task ClearAsync(Guid userId, CancellationToken cancellationToken = default);

    /// Public list of currently-shared locations with the username +
    /// admin marker. Coords are rounded per-row to the user's chosen
    /// precision before being returned.
    Task<IReadOnlyList<SharedLocationDto>> ListAsync(CancellationToken cancellationToken = default);

    /// Returns the caller's current row, or null. Used by /account to show
    /// "you're sharing X" without scanning the public list.
    Task<SharedLocationDto?> GetMineAsync(Guid userId, CancellationToken cancellationToken = default);
}
