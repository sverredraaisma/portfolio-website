namespace PortfolioApi.Configuration;

/// Strongly-typed binding for the "RateLimiting" configuration section.
/// Passed by value into AddPortfolioRateLimiting so the partition factory
/// captures the values once at startup instead of resolving IOptions per request.
public sealed class RateLimitingOptions
{
    public const string Section = "RateLimiting";

    public int PermitLimit { get; set; } = 120;
    public int WindowSeconds { get; set; } = 60;
}
