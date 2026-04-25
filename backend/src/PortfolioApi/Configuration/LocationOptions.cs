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

    /// Decimal places to round coordinates to before serving on the public
    /// list. 3 ≈ 110m precision; close enough to "this person is in this
    /// city" without leaking the home address.
    public int PublicPrecisionDecimals { get; set; } = 3;
}
