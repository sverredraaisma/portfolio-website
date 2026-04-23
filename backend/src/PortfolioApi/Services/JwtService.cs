using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace PortfolioApi.Services;

public class JwtService
{
    private readonly IConfiguration _config;
    private readonly SymmetricSecurityKey _key;

    public JwtService(IConfiguration config)
    {
        _config = config;
        var keyText = config["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key missing");
        _key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(keyText));
    }

    public string CreateAccessToken(Guid userId, string username)
    {
        var minutes = int.Parse(_config["Jwt:AccessTokenMinutes"] ?? "15");
        return Create(new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("username", username),
            new Claim("purpose", "access")
        }, TimeSpan.FromMinutes(minutes));
    }

    public string CreateEmailVerifyToken(Guid userId, string email)
    {
        var hours = int.Parse(_config["Jwt:EmailVerifyHours"] ?? "24");
        return Create(new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim("purpose", "email-verify")
        }, TimeSpan.FromHours(hours));
    }

    public ClaimsPrincipal? Validate(string token, string expectedPurpose)
    {
        var handler = new JwtSecurityTokenHandler();
        try
        {
            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidIssuer = _config["Jwt:Issuer"],
                ValidAudience = _config["Jwt:Audience"],
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
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.Add(lifetime),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
