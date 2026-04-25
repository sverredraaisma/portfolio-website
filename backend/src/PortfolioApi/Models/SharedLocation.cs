namespace PortfolioApi.Models;

/// One row per user — opt-in. The full-precision Latitude/Longitude is
/// stored verbatim, and the public list rounds to PrecisionDecimals before
/// serving — so each user picks their own privacy/utility trade-off (a
/// city-only share for routine pinning, an exact share for a meet-up where
/// friends need to find them in the right building).
///
/// Source distinguishes between "browser" (navigator.geolocation, the user
/// gave the browser permission) and "named" (the user typed a place name
/// and the server geocoded it). A small forensic hint if the user later
/// asks "where did this come from".
///
/// Label is a user-supplied free-form hint shown next to the pin on the
/// public map ("Software developer meetup", "Cafe X"). Bounded length so
/// a hostile client can't dump arbitrary bytes into the public list.
/// Auto-populated from the geocoder display name on a named share when
/// the user didn't supply one.
public class SharedLocation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public User? User { get; set; }

    public double Latitude { get; set; }
    public double Longitude { get; set; }

    public string? Label { get; set; }
    public string Source { get; set; } = "browser"; // browser | named

    /// Decimal places to round Latitude/Longitude to before serving on the
    /// public list. 3 ≈ ~110m (default — same as the previous global
    /// rounding policy); higher values expose more precision, lower values
    /// expose less. Constrained server-side to 0..5 so the choice can't
    /// degenerate into "leak the home address" or "round to nothing".
    public int PrecisionDecimals { get; set; } = 3;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
