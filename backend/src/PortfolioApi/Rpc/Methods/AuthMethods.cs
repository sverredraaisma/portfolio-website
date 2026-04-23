using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PortfolioApi.Data;
using PortfolioApi.Services;

namespace PortfolioApi.Rpc.Methods;

public class AuthMethods
{
    private readonly AuthService _auth;
    private readonly JwtService _jwt;
    private readonly AppDbContext _db;

    public AuthMethods(AuthService auth, JwtService jwt, AppDbContext db)
    {
        _auth = auth;
        _jwt = jwt;
        _db = db;
    }

    public async Task<object?> Register(JsonElement? @params, RpcContext _)
    {
        var p = Required(@params);
        var username = p.GetProperty("username").GetString() ?? throw new InvalidOperationException("username required");
        var email = p.GetProperty("email").GetString() ?? throw new InvalidOperationException("email required");
        var clientHash = p.GetProperty("clientHash").GetString() ?? throw new InvalidOperationException("clientHash required");

        var user = await _auth.RegisterAsync(username, email, clientHash);
        return new { user.Id, user.Username, user.Email, emailVerified = false };
    }

    public async Task<object?> Login(JsonElement? @params, RpcContext _)
    {
        var p = Required(@params);
        var username = p.GetProperty("username").GetString() ?? throw new InvalidOperationException("username required");
        var clientHash = p.GetProperty("clientHash").GetString() ?? throw new InvalidOperationException("clientHash required");

        var user = await _auth.LoginAsync(username, clientHash);
        if (user is null) throw new UnauthorizedAccessException("Invalid credentials");
        if (user.EmailVerifiedAt is null) throw new InvalidOperationException("Email not verified");

        var access = _jwt.CreateAccessToken(user.Id, user.Username);
        var (refresh, _) = await _auth.IssueRefreshTokenAsync(user.Id);

        return new
        {
            accessToken = access,
            refreshToken = refresh,
            user = new { user.Id, user.Username, user.Email }
        };
    }

    public async Task<object?> VerifyEmail(JsonElement? @params, RpcContext _)
    {
        var p = Required(@params);
        var token = p.GetProperty("token").GetString() ?? throw new InvalidOperationException("token required");
        var ok = await _auth.VerifyEmailAsync(token);
        return new { verified = ok };
    }

    public async Task<object?> Me(JsonElement? _, RpcContext ctx)
    {
        var id = ctx.RequireUserId();
        var user = await _db.Users.FindAsync(id) ?? throw new UnauthorizedAccessException("Unknown user");
        return new
        {
            user.Id,
            user.Username,
            user.Email,
            emailVerified = user.EmailVerifiedAt is not null
        };
    }

    private static JsonElement Required(JsonElement? p) =>
        p ?? throw new InvalidOperationException("params required");
}
