namespace PortfolioApi.Configuration;

/// Configuration for the location-sharing feature. The User-Agent is
/// **required** by Nominatim's usage policy — they will block requests
/// without one. A real contact email lets them reach the operator if a
/// burst of requests starts hurting their service.
public sealed class LocationOptions
{
    public const string Section = "Location";

    /// Base URL of the Nominatim instance. Defaults to the public service.
    /// Self-hosters can point this at their own deployment.
    public string NominatimBaseUrl { get; set; } = "https://nominatim.openstreetmap.org";

    /// User-Agent identifying the operator. Set via env in production:
    ///   Location__UserAgent="sverre.dev (sverre@draaisma.dev)"
    public string UserAgent { get; set; } = "PortfolioWebsite (set Location__UserAgent in env)";

    // PublicPrecisionDecimals used to live here as a global default. It is
    // now per-row on SharedLocation so each user picks their own privacy/
    // utility trade-off — see LocationService.MinPrecision/MaxPrecision.
}
