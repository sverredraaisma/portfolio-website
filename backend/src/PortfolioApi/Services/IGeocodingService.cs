namespace PortfolioApi.Services;

public sealed record GeocodeResult(double Latitude, double Longitude, string DisplayName);

/// Thin wrapper over an OpenStreetMap-style geocoder. The interface lets
/// tests swap in a fake without bringing the network into play.
public interface IGeocodingService
{
    /// Returns the first match for <paramref name="query"/>, or null if the
    /// geocoder returned no results / errored. Caller treats null as
    /// "couldn't find that place" and surfaces a clean validation error.
    Task<GeocodeResult?> SearchAsync(string query, CancellationToken cancellationToken = default);
}
