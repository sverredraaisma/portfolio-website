using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using PortfolioApi.Rpc;

namespace PortfolioApi.Tests.Infrastructure;

/// Builds a minimal RpcContext so service-method tests can exercise the
/// authorisation surface (RequireUserId / RequireAdmin / IsAdmin) without
/// spinning up an HTTP server. The context wraps a DefaultHttpContext with a
/// hand-rolled ClaimsPrincipal carrying our normal "sub" / "admin" claims.
public static class TestRpcContext
{
    public static RpcContext Anonymous() => new()
    {
        Http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) }
    };

    public static RpcContext User(Guid userId, bool isAdmin = false)
    {
        var claims = new List<Claim>
        {
            new(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub, userId.ToString()),
            new("admin", isAdmin ? "true" : "false")
        };
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"))
        };
        return new RpcContext { Http = http };
    }

    public static RpcContext Admin(Guid userId) => User(userId, isAdmin: true);
}
