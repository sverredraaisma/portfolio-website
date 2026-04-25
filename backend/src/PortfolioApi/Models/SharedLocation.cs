namespace PortfolioApi.Models;

/// One row per user — opt-in. The full-precision Latitude/Longitude is
/// stored verbatim for forward compatibility (a future "show precise"
/// toggle), but the public list/projection rounds to ~3 decimals (~110m)
/// before serving so home-address-level precision can't be inferred.
///
/// Source distinguishes between "browser" (navigator.geolocation, the user
/// gave the browser permission) and "named" (the user typed a place name
/// and the server geocoded it). A small forensic hint if the user later
/// asks "where did this come from".
///
/// Label is the human-readable hint (city + country from the geocoder, or
/// blank if the user shared raw coords). Bounded length so a hostile client
/// can't dump arbitrary bytes into the public list.
public class SharedLocation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public User? User { get; set; }

    public double Latitude { get; set; }
    public double Longitude { get; set; }

    public string? Label { get; set; }
    public string Source { get; set; } = "browser"; // browser | named

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
