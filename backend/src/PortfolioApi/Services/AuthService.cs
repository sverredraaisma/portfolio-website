using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using PortfolioApi.Data;
using PortfolioApi.Models;

namespace PortfolioApi.Services;

public class AuthService
{
    private const int Argon2Iterations = 3;
    private const int Argon2MemoryKb = 65536; // 64 MiB
    private const int Argon2Parallelism = 2;
    private const int Argon2HashSize = 32;
    private const int SaltSize = 16;

    private readonly AppDbContext _db;
    private readonly JwtService _jwt;
    private readonly EmailService _email;

    public AuthService(AppDbContext db, JwtService jwt, EmailService email)
    {
        _db = db;
        _jwt = jwt;
        _email = email;
    }

    public async Task<User> RegisterAsync(string username, string email, string clientHashHex)
    {
        RejectIfLooksLikeRawPassword(clientHashHex);

        if (await _db.Users.AnyAsync(u => u.Username == username))
            throw new InvalidOperationException("Username taken");
        if (await _db.Users.AnyAsync(u => u.Email == email))
            throw new InvalidOperationException("Email taken");

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
        await _db.SaveChangesAsync();

        var token = _jwt.CreateEmailVerifyToken(user.Id, user.Email);
        await _email.SendVerificationAsync(user.Email, token);

        return user;
    }

    public async Task<User?> LoginAsync(string username, string clientHashHex)
    {
        RejectIfLooksLikeRawPassword(clientHashHex);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username);
        if (user is null) return null;

        var attempt = Argon2(HexToBytes(clientHashHex), user.PasswordSalt);
        return CryptographicOperations.FixedTimeEquals(attempt, user.PasswordHash) ? user : null;
    }

    public async Task<bool> VerifyEmailAsync(string jwtToken)
    {
        var principal = _jwt.Validate(jwtToken, expectedPurpose: "email-verify");
        if (principal is null) return false;

        var sub = principal.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
        if (!Guid.TryParse(sub, out var userId)) return false;

        var user = await _db.Users.FindAsync(userId);
        if (user is null) return false;

        user.EmailVerifiedAt ??= DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<(string token, RefreshToken stored)> IssueRefreshTokenAsync(Guid userId)
    {
        var raw = RandomNumberGenerator.GetBytes(48);
        var rawText = Convert.ToBase64String(raw);
        var hash = SHA256.HashData(raw);

        var rt = new RefreshToken
        {
            UserId = userId,
            TokenHash = hash,
            ExpiresAt = DateTime.UtcNow.AddDays(30)
        };
        _db.RefreshTokens.Add(rt);
        await _db.SaveChangesAsync();
        return (rawText, rt);
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
