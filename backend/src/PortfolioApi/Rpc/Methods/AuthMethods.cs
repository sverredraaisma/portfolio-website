using PortfolioApi.Data;
using PortfolioApi.Services;

namespace PortfolioApi.Rpc.Methods;

// ---- Param records ---------------------------------------------------------

/// Required-keyword properties: missing fields throw on deserialisation, which
/// the typed adapter translates into a 400 with the offending field name.
public sealed record RegisterParams
{
    public required string Username { get; init; }
    public required string Email { get; init; }
    public required string ClientHash { get; init; }
}

public sealed record LoginParams
{
    public required string Username { get; init; }
    public required string ClientHash { get; init; }
}

public sealed record RefreshParams
{
    public required string RefreshToken { get; init; }
}

public sealed record LogoutParams
{
    public required string RefreshToken { get; init; }
}

public sealed record VerifyEmailParams
{
    public required string Token { get; init; }
}

public sealed record RequestPasswordResetParams
{
    public required string Email { get; init; }
}

public sealed record ResetPasswordParams
{
    public required string Token { get; init; }
    public required string ClientHash { get; init; }
}

// ---- Response records ------------------------------------------------------

public sealed record UserDto(Guid Id, string Username, string Email, bool EmailVerified, bool IsAdmin);
public sealed record RegisterResult(Guid Id, string Username, bool EmailVerified);
public sealed record AuthSuccess(string AccessToken, string RefreshToken, UserDto User);
public sealed record VerifyResult(bool Verified);

public class AuthMethods
{
    private readonly IAuthService _auth;
    private readonly IJwtService _jwt;
    private readonly AppDbContext _db;

    public AuthMethods(IAuthService auth, IJwtService jwt, AppDbContext db)
    {
        _auth = auth;
        _jwt = jwt;
        _db = db;
    }

    public async Task<RegisterResult> Register(RegisterParams p, RpcContext ctx)
    {
        var user = await _auth.RegisterAsync(p.Username, p.Email, p.ClientHash, ctx.CancellationToken);

        // Don't echo the email back — frontend already has it. Keep response minimal.
        return new RegisterResult(user.Id, user.Username, EmailVerified: false);
    }

    public async Task<AuthSuccess> Login(LoginParams p, RpcContext ctx)
    {
        var user = await _auth.LoginAsync(p.Username, p.ClientHash, ctx.CancellationToken);
        if (user is null) throw new AuthFailedException("Invalid credentials");

        if (user.EmailVerifiedAt is null)
        {
            // Distinguished from "invalid credentials" — anyone seeing this already
            // got past the password check, so they own the account.
            throw new InvalidOperationException("Email not verified");
        }

        var access = _jwt.CreateAccessToken(user.Id, user.Username, user.IsAdmin);
        var (refresh, _) = await _auth.IssueRefreshTokenAsync(user.Id, ctx.CancellationToken);

        return new AuthSuccess(
            access,
            refresh,
            new UserDto(user.Id, user.Username, user.Email, EmailVerified: true, user.IsAdmin));
    }

    public async Task<AuthSuccess> Refresh(RefreshParams p, RpcContext ctx)
    {
        var (tokens, user) = await _auth.RefreshAsync(p.RefreshToken, ctx.CancellationToken);
        return new AuthSuccess(
            tokens.AccessToken,
            tokens.RefreshToken,
            new UserDto(user.Id, user.Username, user.Email, EmailVerified: user.EmailVerifiedAt is not null, user.IsAdmin));
    }

    public async Task<OkResult> Logout(LogoutParams p, RpcContext ctx)
    {
        await _auth.LogoutAsync(p.RefreshToken, ctx.CancellationToken);
        return new OkResult();
    }

    public async Task<VerifyResult> VerifyEmail(VerifyEmailParams p, RpcContext ctx)
    {
        var ok = await _auth.VerifyEmailAsync(p.Token, ctx.CancellationToken);
        return new VerifyResult(ok);
    }

    public async Task<OkResult> RequestPasswordReset(RequestPasswordResetParams p, RpcContext ctx)
    {
        // Always returns ok — the service silently no-ops for unknown emails so
        // attackers can't enumerate which addresses have accounts.
        await _auth.RequestPasswordResetAsync(p.Email, ctx.CancellationToken);
        return new OkResult();
    }

    public async Task<OkResult> ResetPassword(ResetPasswordParams p, RpcContext ctx)
    {
        await _auth.ResetPasswordAsync(p.Token, p.ClientHash, ctx.CancellationToken);
        return new OkResult();
    }

    public async Task<UserDto> Me(RpcContext ctx)
    {
        var id = ctx.RequireUserId();
        var user = await _db.Users.FindAsync(new object?[] { id }, ctx.CancellationToken)
            ?? throw new AuthFailedException("Unknown user");
        return new UserDto(user.Id, user.Username, user.Email, EmailVerified: user.EmailVerifiedAt is not null, user.IsAdmin);
    }
}
