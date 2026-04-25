using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using PortfolioApi.Configuration;

namespace PortfolioApi.Services;

/// Hits Nominatim (the OSM-blessed geocoder) for named-place lookups. Two
/// concessions to OSM's usage policy:
///   1. A real User-Agent (required; missing UA → 403).
///   2. An in-memory cache so repeated lookups for the same query aren't
///      forwarded — also smooths over the 1 req/s rate limit when several
///      users type the same city.
public sealed class NominatimGeocodingService : IGeocodingService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly LocationOptions _opt;

    public NominatimGeocodingService(HttpClient http, IMemoryCache cache, IOptions<LocationOptions> opt)
    {
        _http = http;
        _cache = cache;
        _opt = opt.Value;

        // The UA can also be set via the registered HttpClient's default
        // headers; setting it here too is a belt-and-braces fallback so a
        // misconfigured DI graph still produces a working request.
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", _opt.UserAgent);
    }

    public async Task<GeocodeResult?> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        var trimmed = (query ?? "").Trim();
        if (trimmed.Length == 0 || trimmed.Length > 200) return null;

        var cacheKey = "geo:" + trimmed.ToLowerInvariant();
        if (_cache.TryGetValue<GeocodeResult>(cacheKey, out var hit)) return hit;

        var url = $"{_opt.NominatimBaseUrl.TrimEnd('/')}/search?format=json&limit=1&q={Uri.EscapeDataString(trimmed)}";
        try
        {
            var response = await _http.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            var rows = await response.Content.ReadFromJsonAsync<NominatimRow[]>(cancellationToken);
            if (rows is null || rows.Length == 0) return null;

            var first = rows[0];
            // Nominatim returns lat/lon as strings — explicitly invariant
            // parse so a system locale that uses comma as the decimal
            // separator doesn't trip us up.
            if (!double.TryParse(first.lat, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lat)) return null;
            if (!double.TryParse(first.lon, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lon)) return null;

            var result = new GeocodeResult(lat, lon, first.display_name ?? trimmed);
            _cache.Set(cacheKey, result, CacheTtl);
            return result;
        }
        catch (Exception)
        {
            // Caller treats "couldn't find that place" identically to "the
            // geocoder is down" — both surface as a generic validation error.
            return null;
        }
    }

    // Property names mirror Nominatim's wire format — System.Text.Json's
    // case-insensitive default doesn't help here because the names are
    // already lowercase.
    private sealed record NominatimRow(string? lat, string? lon, string? display_name);
}
