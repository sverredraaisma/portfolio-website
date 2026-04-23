using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PortfolioApi.Configuration;
using PortfolioApi.Constants;

namespace PortfolioApi.Services;

public class JwtService : IJwtService
{
    private readonly JwtOptions _opt;
    private readonly SymmetricSecurityKey _key;

    public JwtService(IOptions<JwtOptions> opt)
    {
        _opt = opt.Value;
        _key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opt.Key));
    }

    public string CreateAccessToken(Guid userId, string username, bool isAdmin)
    {
        return Create(new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("username", username),
            new Claim("admin", isAdmin ? "true" : "false"),
            new Claim("purpose", JwtPurpose.Access)
        }, TimeSpan.FromMinutes(_opt.AccessTokenMinutes));
    }

    public string CreateEmailVerifyToken(Guid userId, string email)
    {
        return Create(new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim("purpose", JwtPurpose.EmailVerify)
        }, TimeSpan.FromHours(_opt.EmailVerifyHours));
    }

    public string CreatePasswordResetToken(Guid userId)
    {
        return Create(new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("purpose", JwtPurpose.PasswordReset)
        }, TimeSpan.FromHours(_opt.PasswordResetHours));
    }

    public ClaimsPrincipal? Validate(string token, string expectedPurpose)
    {
        // Clear the default inbound claim-type map. Without this, JwtSecurityTokenHandler
        // rewrites "sub" -> ClaimTypes.NameIdentifier, "email" -> ClaimTypes.Email, etc,
        // so principal.FindFirst("sub") returns null for our own tokens.
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        try
        {
            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidIssuer = _opt.Issuer,
                ValidAudience = _opt.Audience,
                IssuerSigningKey = _key
            }, out _);

            if (principal.FindFirst("purpose")?.Value != expectedPurpose) return null;
            return principal;
        }
        catch
        {
            return null;
        }
    }

    private string Create(IEnumerable<Claim> claims, TimeSpan lifetime)
    {
        var creds = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _opt.Issuer,
            audience: _opt.Audience,
            claims: claims,
            expires: DateTime.UtcNow.Add(lifetime),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
