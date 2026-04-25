namespace PortfolioApi.Configuration;

/// Site-wide non-secret options. PublicOrigin is the trusted origin used
/// to build absolute URLs in feeds (RSS / Atom / sitemap). Pinning it here
/// prevents a hostile Host header on a poll request from poisoning the
/// links a crawler then ingests. Empty falls back to the request Scheme +
/// Host — fine in dev, not in production behind an untrusted forwarder.
public sealed class SiteOptions
{
    public const string Section = "Site";

    /// e.g. "https://sverre.dev". Empty = use request Host (dev fallback).
    public string PublicOrigin { get; set; } = string.Empty;
}
