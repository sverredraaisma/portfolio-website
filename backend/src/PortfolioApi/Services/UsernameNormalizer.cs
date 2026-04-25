using System.Text.RegularExpressions;

namespace PortfolioApi.Services;

/// Single source of truth for username rules.
///
/// Stored form: lowercase, [a-z0-9_-], 3–32 chars, must start and end with
/// an alphanumeric. Registration enforces these strictly so two accounts
/// like "Alice" and "alice" can't coexist (the DB unique index on Username
/// is byte-exact and case-sensitive).
///
/// Lookups (login, passkey assertion, profile fetch) accept any casing and
/// canonicalise before comparing — typing "ALICE" finds "alice".
public static class UsernameNormalizer
{
    public const int MinLength = 3;
    public const int MaxLength = 32;

    // Anchored. Must start and end alphanumeric so we don't accept "_alice"
    // or "alice-" — those interact badly with URL routing and visual
    // presentation. The middle range allows underscore and dash.
    private static readonly Regex Pattern = new(
        @"^[a-z0-9](?:[a-z0-9_-]{1,30}[a-z0-9])?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// Normalises the candidate for *registration* — strict. Throws
    /// InvalidOperationException with a user-safe message if the value
    /// can't be stored as-is.
    public static string NormaliseForRegister(string? raw)
    {
        var trimmed = (raw ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            throw new InvalidOperationException("username required");
        if (trimmed.Length < MinLength || trimmed.Length > MaxLength)
            throw new InvalidOperationException($"username must be {MinLength}-{MaxLength} characters");

        // Reject mixed case explicitly so the user knows their preferred
        // capitalisation isn't being silently changed under them.
        var lowered = trimmed.ToLowerInvariant();
        if (lowered != trimmed)
            throw new InvalidOperationException("username must be lowercase");

        if (!Pattern.IsMatch(lowered))
            throw new InvalidOperationException(
                "username may contain a-z, 0-9, '_' and '-' (must start and end with a letter or digit)");

        return lowered;
    }

    /// Normalises a value coming in from a *lookup* (login, profile fetch,
    /// passkey assertion). Permissive: lowercases and trims, returns null
    /// if the input couldn't possibly match a stored username — saves a
    /// round-trip and avoids feeding obviously invalid values to a SQL
    /// LIKE/equality.
    public static string? NormaliseForLookup(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var lowered = raw.Trim().ToLowerInvariant();
        if (lowered.Length < MinLength || lowered.Length > MaxLength) return null;
        if (!Pattern.IsMatch(lowered)) return null;
        return lowered;
    }
}
