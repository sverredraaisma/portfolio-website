using System.Security.Cryptography;
using Konscious.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PortfolioApi.Configuration;
using PortfolioApi.Constants;
using PortfolioApi.Data;
using PortfolioApi.Models;

namespace PortfolioApi.Services;

public class AuthService : IAuthService
{
    private const int Argon2Iterations = 3;
    private const int Argon2MemoryKb = 65536; // 64 MiB
    private const int Argon2Parallelism = 2;
    private const int Argon2HashSize = 32;
    private const int SaltSize = 16;
    private const int RefreshTokenBytes = 48;

    private readonly AppDbContext _db;
    private readonly IJwtService _jwt;
    private readonly IEmailService _email;
    private readonly JwtOptions _jwtOpt;

    public AuthService(AppDbContext db, IJwtService jwt, IEmailService email, IOptions<JwtOptions> jwtOpt)
    {
        _db = db;
        _jwt = jwt;
        _email = email;
        _jwtOpt = jwtOpt.Value;
    }

    public async Task<User> RegisterAsync(string username, string email, string clientHashHex, CancellationToken cancellationToken = default)
    {
        RejectIfLooksLikeRawPassword(clientHashHex);

        // Username clashes are exposed (usernames are public anyway).
        // Email clashes are NOT exposed — that would let an attacker enumerate
        // which addresses have accounts. We rely on the unique-index +
        // DbUpdateException catch below for the email case.
        if (await _db.Users.AnyAsync(u => u.Username == username, cancellationToken))
            throw new InvalidOperationException("Username taken");

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Argon2(HexToBytes(clientHashHex), salt);

        var user = new User
        {
            Username = username,
            Email = email,
            PasswordHash = hash,
            PasswordSalt = salt
        };

        _db.Users.Add(user);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Likely an email-uniqueness violation. Tell the user something
            // generic — never confirm whether the address is in use.
            throw new InvalidOperationException(
                "Registration could not be completed. If you already have an account, sign in or reset your password.");
        }

        var token = _jwt.CreateEmailVerifyToken(user.Id, user.Email);
        await _email.SendVerificationAsync(user.Email, token);

        return user;
    }

    public async Task<User?> LoginAsync(string username, string clientHashHex, CancellationToken cancellationToken = default)
    {
        RejectIfLooksLikeRawPassword(clientHashHex);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username, cancellationToken);
        if (user is null)
        {
            // Run a dummy hash so the response time matches the success path —
            // mitigates username-enumeration via timing.
            _ = Argon2(HexToBytes(clientHashHex), new byte[SaltSize]);
            return null;
        }

        var attempt = Argon2(HexToBytes(clientHashHex), user.PasswordSalt);
        return CryptographicOperations.FixedTimeEquals(attempt, user.PasswordHash) ? user : null;
    }

    public async Task<bool> VerifyEmailAsync(string jwtToken, CancellationToken cancellationToken = default)
    {
        var principal = _jwt.Validate(jwtToken, expectedPurpose: JwtPurpose.EmailVerify);
        if (principal is null) return false;

        var sub = principal.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
        if (!Guid.TryParse(sub, out var userId)) return false;

        var user = await _db.Users.FindAsync(new object?[] { userId }, cancellationToken);
        if (user is null) return false;

        user.EmailVerifiedAt ??= DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<(string token, RefreshToken stored)> IssueRefreshTokenAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var raw = RandomNumberGenerator.GetBytes(RefreshTokenBytes);
        var rawText = Convert.ToBase64String(raw);
        var hash = SHA256.HashData(raw);

        var rt = new RefreshToken
        {
            UserId = userId,
            TokenHash = hash,
            ExpiresAt = DateTime.UtcNow.AddDays(_jwtOpt.RefreshTokenDays)
        };
        _db.RefreshTokens.Add(rt);
        await _db.SaveChangesAsync(cancellationToken);
        return (rawText, rt);
    }

    public async Task<(AuthTokens tokens, User user)> RefreshAsync(string rawRefreshToken, CancellationToken cancellationToken = default)
    {
        var hash = HashRefresh(rawRefreshToken);

        var stored = await _db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken)
            ?? throw new AuthFailedException("Refresh token not found");

        if (stored.RevokedAt is not null) throw new AuthFailedException("Refresh token revoked");
        if (stored.ExpiresAt < DateTime.UtcNow) throw new AuthFailedException("Refresh token expired");
        if (stored.User is null) throw new AuthFailedException("Refresh token has no user");

        // Rotate: revoke this token and issue a new one in the same transaction.
        stored.RevokedAt = DateTime.UtcNow;
        var (newRaw, _) = await IssueRefreshTokenAsync(stored.UserId, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        var access = _jwt.CreateAccessToken(stored.User.Id, stored.User.Username, stored.User.IsAdmin);
        return (new AuthTokens(access, newRaw), stored.User);
    }

    public async Task LogoutAsync(string rawRefreshToken, CancellationToken cancellationToken = default)
    {
        var hash = HashRefresh(rawRefreshToken);
        var stored = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (stored is null || stored.RevokedAt is not null) return;

        stored.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static byte[] HashRefresh(string raw)
    {
        try { return SHA256.HashData(Convert.FromBase64String(raw)); }
        catch (FormatException) { throw new AuthFailedException("Malformed refresh token"); }
    }

    private static byte[] Argon2(byte[] password, byte[] salt)
    {
        using var argon = new Argon2id(password)
        {
            Salt = salt,
            DegreeOfParallelism = Argon2Parallelism,
            Iterations = Argon2Iterations,
            MemorySize = Argon2MemoryKb
        };
        return argon.GetBytes(Argon2HashSize);
    }

    private static byte[] HexToBytes(string hex)
    {
        if (hex.Length % 2 != 0) throw new ArgumentException("Invalid client hash");
        var bytes = new byte[hex.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return bytes;
    }

    // The client hash is sha256 → always 64 hex chars. Anything else is suspicious; refuse.
    private static void RejectIfLooksLikeRawPassword(string clientHashHex)
    {
        if (clientHashHex.Length != 64 || !IsHex(clientHashHex))
            throw new InvalidOperationException("clientHash must be a 64-char hex sha256 digest");
    }

    private static bool IsHex(string s)
    {
        foreach (var c in s)
            if (!Uri.IsHexDigit(c)) return false;
        return true;
    }
}
