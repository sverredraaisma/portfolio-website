using System.ComponentModel.DataAnnotations;

namespace PortfolioApi.Configuration;

/// Strongly-typed binding for the "Jwt" configuration section.
/// Bound + validated at startup; the app refuses to boot if Issuer/Audience/Key
/// are missing or the key is too short for HS256.
public sealed class JwtOptions
{
    public const string Section = "Jwt";

    [Required]
    public string Issuer { get; set; } = string.Empty;

    [Required]
    public string Audience { get; set; } = string.Empty;

    [Required, MinLength(32)]
    public string Key { get; set; } = string.Empty;

    [Range(1, 1440)]
    public int AccessTokenMinutes { get; set; } = 15;

    [Range(1, 365)]
    public int RefreshTokenDays { get; set; } = 30;

    [Range(1, 168)]
    public int EmailVerifyHours { get; set; } = 24;

    [Range(1, 24)]
    public int PasswordResetHours { get; set; } = 1;
}
