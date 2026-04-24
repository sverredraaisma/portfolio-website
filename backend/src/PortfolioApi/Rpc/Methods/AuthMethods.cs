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

public sealed record ResendVerificationParams
{
    public required string Email { get; init; }
}

public sealed record ChangePasswordParams
{
    public required string CurrentClientHash { get; init; }
    public required string NewClientHash { get; init; }
}

public sealed record RequestEmailChangeParams
{
    public required string NewEmail { get; init; }
}

public sealed record ConfirmEmailChangeParams
{
    public required string Token { get; init; }
}

public sealed record CompleteTotpParams
{
    public required string Challenge { get; init; }
    public required string Code { get; init; }
}

public sealed record TotpCodeParams
{
    public required string Code { get; init; }
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

/// Either auth completes (Tokens populated) or a TOTP step is required
/// (Challenge populated). Exactly one of the two is non-null.
public sealed record LoginResponse(AuthSuccess? Tokens, string? Challenge)
{
    public bool RequiresTotp => Challenge is not null;
}

public sealed record TotpEnrolmentDto(string OtpAuthUri, string SecretBase32);

public sealed record RecoveryCodesDto(IReadOnlyList<string> Codes);

public sealed record TotpConfirmResult(IReadOnlyList<string> RecoveryCodes);

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

    public async Task<LoginResponse> Login(LoginParams p, RpcContext ctx)
    {
        var stage = await _auth.BeginLoginAsync(p.Username, p.ClientHash, ctx.CancellationToken);

        if (stage.RequiresTotp)
        {
            // Password was correct but TOTP is required — bounce the client
            // through auth.completeTotp. Email-verification check happens at
            // the second step so we don't leak the unverified state to a
            // caller who hasn't yet proven possession of the second factor.
            return new LoginResponse(null, stage.ChallengeToken);
        }

        var user = stage.User ?? throw new AuthFailedException("Invalid credentials");

        if (user.EmailVerifiedAt is null)
        {
            // Distinguished from "invalid credentials" — anyone seeing this already
            // got past the password check, so they own the account.
            throw new InvalidOperationException("Email not verified");
        }

        return new LoginResponse(await IssueSession(user, ctx), null);
    }

    public async Task<LoginResponse> CompleteTotp(CompleteTotpParams p, RpcContext ctx)
    {
        var user = await _auth.CompleteTotpAsync(p.Challenge, p.Code, ctx.CancellationToken);

        if (user.EmailVerifiedAt is null)
            throw new InvalidOperationException("Email not verified");

        return new LoginResponse(await IssueSession(user, ctx), null);
    }

    private async Task<AuthSuccess> IssueSession(PortfolioApi.Models.User user, RpcContext ctx)
    {
        var access = _jwt.CreateAccessToken(user.Id, user.Username, user.IsAdmin);
        var (refresh, _) = await _auth.IssueRefreshTokenAsync(user.Id, ctx.CancellationToken);
        return new AuthSuccess(
            access,
            refresh,
            new UserDto(user.Id, user.Username, user.Email, EmailVerified: true, user.IsAdmin));
    }

    public async Task<TotpEnrolmentDto> TotpStart(RpcContext ctx)
    {
        var userId = ctx.RequireUserId();
        var enrol = await _auth.StartTotpEnrolmentAsync(userId, ctx.CancellationToken);
        return new TotpEnrolmentDto(enrol.OtpAuthUri, enrol.Base32Secret);
    }

    public async Task<TotpConfirmResult> TotpConfirm(TotpCodeParams p, RpcContext ctx)
    {
        var userId = ctx.RequireUserId();
        await _auth.ConfirmTotpEnrolmentAsync(userId, p.Code, ctx.CancellationToken);
        // Issue an initial sheet of recovery codes immediately on enrolment;
        // the client must show them once and tell the user to save them.
        var codes = await _auth.RegenerateRecoveryCodesAsync(userId, ctx.CancellationToken);
        return new TotpConfirmResult(codes);
    }

    public async Task<RecoveryCodesDto> TotpRegenerateRecoveryCodes(RpcContext ctx)
    {
        var userId = ctx.RequireUserId();
        var codes = await _auth.RegenerateRecoveryCodesAsync(userId, ctx.CancellationToken);
        return new RecoveryCodesDto(codes);
    }

    public async Task<OkResult> TotpDisable(TotpCodeParams p, RpcContext ctx)
    {
        var userId = ctx.RequireUserId();
        await _auth.DisableTotpAsync(userId, p.Code, ctx.CancellationToken);
        return new OkResult();
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

    public async Task<OkResult> ResendVerification(ResendVerificationParams p, RpcContext ctx)
    {
        // Same enumeration-resistance pattern as password reset.
        await _auth.ResendVerificationAsync(p.Email, ctx.CancellationToken);
        return new OkResult();
    }

    public async Task<OkResult> ChangePassword(ChangePasswordParams p, RpcContext ctx)
    {
        var userId = ctx.RequireUserId();
        await _auth.ChangePasswordAsync(userId, p.CurrentClientHash, p.NewClientHash, ctx.CancellationToken);
        return new OkResult();
    }

    public async Task<OkResult> RevokeAllSessions(RpcContext ctx)
    {
        var userId = ctx.RequireUserId();
        await _auth.RevokeAllSessionsAsync(userId, ctx.CancellationToken);
        return new OkResult();
    }

    public async Task<OkResult> RequestEmailChange(RequestEmailChangeParams p, RpcContext ctx)
    {
        var userId = ctx.RequireUserId();
        await _auth.RequestEmailChangeAsync(userId, p.NewEmail, ctx.CancellationToken);
        return new OkResult();
    }

    public async Task<VerifyResult> ConfirmEmailChange(ConfirmEmailChangeParams p, RpcContext ctx)
    {
        // Reuses the VerifyResult shape — same boolean meaning.
        var ok = await _auth.ConfirmEmailChangeAsync(p.Token, ctx.CancellationToken);
        return new VerifyResult(ok);
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
