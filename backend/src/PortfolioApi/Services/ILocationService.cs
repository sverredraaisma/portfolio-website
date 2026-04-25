namespace PortfolioApi.Services;

/// Public-facing list row. Coords are already rounded to the
/// configured precision so callers don't need to do it themselves.
public sealed record SharedLocationDto(
    string Username,
    bool IsAdmin,
    double Latitude,
    double Longitude,
    string? Label,
    DateTime UpdatedAt);

public interface ILocationService
{
    /// Set or update the caller's shared location. Validates the supplied
    /// lat/lon ranges; throws InvalidOperationException on bad input.
    Task SetCoordsAsync(Guid userId, double latitude, double longitude, string? label, CancellationToken cancellationToken = default);

    /// Geocode a place name via the IGeocodingService and store the result.
    /// Throws InvalidOperationException when the geocoder finds no match.
    Task SetByNameAsync(Guid userId, string placeName, CancellationToken cancellationToken = default);

    /// Removes the caller's row. No-op if no row exists.
    Task ClearAsync(Guid userId, CancellationToken cancellationToken = default);

    /// Public list of currently-shared locations with the username +
    /// admin marker. Coords already rounded to the public precision.
    Task<IReadOnlyList<SharedLocationDto>> ListAsync(CancellationToken cancellationToken = default);

    /// Returns the caller's current row, or null. Used by /account to show
    /// "you're sharing X" without scanning the public list.
    Task<SharedLocationDto?> GetMineAsync(Guid userId, CancellationToken cancellationToken = default);
}
