using System.Security.Cryptography;
using Konscious.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PortfolioApi.Configuration;
using PortfolioApi.Constants;
using PortfolioApi.Data;
using PortfolioApi.Models;

// Audit constants live alongside the rest of the constants — pull both up front.

namespace PortfolioApi.Services;

public class AuthService : IAuthService
{
    private const int Argon2Iterations = 3;
    private const int Argon2MemoryKb = 65536; // 64 MiB
    private const int Argon2Parallelism = 2;
    private const int Argon2HashSize = 32;
    private const int SaltSize = 16;
    private const int RefreshTokenBytes = 48;
    private const int RecoveryCodeCount = 10;
    // 5 base32 chars per group × 2 groups = 10 chars of entropy per code,
    // ~50 bits — plenty to survive 10 codes' worth of guessing under rate
    // limits. Display format inserts the dash for readability.
    private const int RecoveryCodeGroupChars = 5;

    private readonly AppDbContext _db;
    private readonly IJwtService _jwt;
    private readonly IEmailService _email;
    private readonly ITotpService _totp;
    private readonly IAuditService _audit;
    private readonly ILoginThrottle _throttle;
    private readonly JwtOptions _jwtOpt;

    public AuthService(AppDbContext db, IJwtService jwt, IEmailService email, ITotpService totp, IAuditService audit, ILoginThrottle throttle, IOptions<JwtOptions> jwtOpt)
    {
        _db = db;
        _jwt = jwt;
        _email = email;
        _totp = totp;
        _audit = audit;
        _throttle = throttle;
        _jwtOpt = jwtOpt.Value;
    }

    public async Task<User> RegisterAsync(string username, string email, string clientHashHex, CancellationToken cancellationToken = default)
    {
        RejectIfLooksLikeRawPassword(clientHashHex);

        // Strict-form normalisation: rejects mixed case, bad chars, length
        // out of range. Throws a user-safe InvalidOperationException that
        // the router maps to a 400.
        username = UsernameNormalizer.NormaliseForRegister(username);

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
        user.EmailVerifySentAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await _email.SendVerificationAsync(user.Email, token);

        return user;
    }

    public async Task<User?> LoginAsync(string username, string clientHashHex, CancellationToken cancellationToken = default)
    {
        RejectIfLooksLikeRawPassword(clientHashHex);

        // Permissive normalisation: typing "ALICE" finds "alice". A value
        // that can't be a valid username at all (whitespace, weird chars)
        // short-circuits here — but we still run the dummy hash below so
        // the timing matches the unknown-user path.
        var key = UsernameNormalizer.NormaliseForLookup(username) ?? string.Empty;

        // Per-username throttle keyed off the canonical form so "Alice" and
        // "alice" share the same lockout counter. Throws AuthFailedException
        // with the same code the router maps to 401 — same shape as bad
        // creds, so an attacker can't tell from the outside whether they're
        // locked out vs wrong.
        _throttle.EnsureNotLocked(key);

        var user = key.Length == 0
            ? null
            : await _db.Users.FirstOrDefaultAsync(u => u.Username == key, cancellationToken);
        if (user is null)
        {
            // Run a dummy hash so the response time matches the success path —
            // mitigates username-enumeration via timing.
            _ = Argon2(HexToBytes(clientHashHex), new byte[SaltSize]);
            _throttle.RecordFailure(key);
            return null;
        }

        var attempt = Argon2(HexToBytes(clientHashHex), user.PasswordSalt);
        if (!CryptographicOperations.FixedTimeEquals(attempt, user.PasswordHash))
        {
            _throttle.RecordFailure(key);
            return null;
        }

        // Note: we do NOT clear the throttle here — TOTP-enabled accounts
        // still have a second factor to clear before the login is final. The
        // throttle is cleared by the caller (BeginLoginAsync / CompleteTotpAsync)
        // once the full handshake succeeds.
        return user;
    }

    public async Task<LoginStageResult> BeginLoginAsync(string username, string clientHashHex, CancellationToken cancellationToken = default)
    {
        var user = await LoginAsync(username, clientHashHex, cancellationToken);
        if (user is null) return new LoginStageResult(null, null);

        // Email-not-verified is enforced by the caller (AuthMethods.Login)
        // because the message there is informational; here we only mediate
        // the TOTP fork.
        if (user.TotpEnabledAt is not null)
        {
            // Don't clear the throttle yet — the second factor still has to
            // succeed. CompleteTotp clears once the whole handshake is done.
            var challenge = _jwt.CreateTotpChallengeToken(user.Id);
            return new LoginStageResult(null, challenge);
        }

        _throttle.Clear(username);
        return new LoginStageResult(user, null);
    }

    public async Task<User> CompleteTotpAsync(string challengeToken, string code, CancellationToken cancellationToken = default)
    {
        var principal = _jwt.Validate(challengeToken, expectedPurpose: JwtPurpose.TotpChallenge)
            ?? throw new AuthFailedException("Invalid or expired challenge");
        var sub = principal.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
        if (!Guid.TryParse(sub, out var userId)) throw new AuthFailedException("Challenge has no subject");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new AuthFailedException("Unknown user");

        if (user.TotpEnabledAt is null || user.TotpSecret is null)
            throw new AuthFailedException("TOTP is not enabled");

        // Throttle the TOTP step too — otherwise an attacker who gets the
        // password (or steals a challenge) can grind 6-digit codes freely.
        _throttle.EnsureNotLocked(user.Username);

        var trimmed = (code ?? "").Trim();

        // Branch on shape: 6 digits → TOTP code; anything else → try recovery.
        // Both paths must be present; a TOTP attempt that fails should not
        // leak through the recovery path and vice versa.
        if (trimmed.Length == 6 && trimmed.All(char.IsDigit))
        {
            if (_totp.Verify(user.TotpSecret, trimmed))
            {
                _throttle.Clear(user.Username);
                return user;
            }
            _throttle.RecordFailure(user.Username);
            throw new AuthFailedException("Invalid TOTP code");
        }

        // Recovery code: normalise (uppercase, drop dashes/spaces), look up
        // the SHA-256, mark used. Single-use.
        var normalised = NormaliseRecoveryCode(trimmed);
        if (normalised.Length == 0)
        {
            _throttle.RecordFailure(user.Username);
            throw new AuthFailedException("Invalid code");
        }

        var hash = SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(normalised));
        var match = await _db.RecoveryCodes
            .FirstOrDefaultAsync(r => r.UserId == userId && r.UsedAt == null && r.CodeHash == hash, cancellationToken);
        if (match is null)
        {
            _throttle.RecordFailure(user.Username);
            throw new AuthFailedException("Invalid code");
        }

        match.UsedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        _throttle.Clear(user.Username);
        return user;
    }

    public async Task<IReadOnlyList<string>> RegenerateRecoveryCodesAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new AuthFailedException("Unknown user");

        if (user.TotpEnabledAt is null)
            throw new InvalidOperationException("TOTP is not enabled");

        // Wipe the old set (used or not — generating a new sheet should fully
        // supersede the previous one) and insert a fresh batch in one round-trip.
        await _db.RecoveryCodes
            .Where(r => r.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        var raw = new List<string>(RecoveryCodeCount);
        for (int i = 0; i < RecoveryCodeCount; i++) raw.Add(GenerateRecoveryCode());

        var rows = raw.Select(code => new RecoveryCode
        {
            UserId = userId,
            CodeHash = SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(NormaliseRecoveryCode(code)))
        });
        _db.RecoveryCodes.AddRange(rows);
        _audit.Record(userId, AuditKind.RecoveryCodesRegenerated);
        await _db.SaveChangesAsync(cancellationToken);
        return raw;
    }

    public Task<int> CountRemainingRecoveryCodesAsync(Guid userId, CancellationToken cancellationToken = default) =>
        _db.RecoveryCodes.CountAsync(r => r.UserId == userId && r.UsedAt == null, cancellationToken);

    // 10 base32 chars, displayed as XXXXX-XXXXX. Lowercase 'l' / digit '1' /
    // 'o' / '0' aren't in the base32 alphabet to begin with so there's no
    // confusable-character cleanup to do.
    private static string GenerateRecoveryCode()
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"; // RFC 4648 base32
        var total = RecoveryCodeGroupChars * 2;
        var bytes = RandomNumberGenerator.GetBytes(total);
        var chars = new char[total + 1];
        for (int i = 0; i < total; i++) chars[i < RecoveryCodeGroupChars ? i : i + 1] = alphabet[bytes[i] % alphabet.Length];
        chars[RecoveryCodeGroupChars] = '-';
        return new string(chars);
    }

    // Strip dashes / whitespace and uppercase so the user can type the code
    // however they like. Drop anything outside the base32 alphabet so the
    // hash lookup is over a normalised value.
    private static string NormaliseRecoveryCode(string raw)
    {
        var sb = new System.Text.StringBuilder(raw.Length);
        foreach (var ch in raw.ToUpperInvariant())
            if ((ch >= 'A' && ch <= 'Z') || (ch >= '2' && ch <= '7'))
                sb.Append(ch);
        return sb.ToString();
    }

    public async Task<TotpEnrolment> StartTotpEnrolmentAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new AuthFailedException("Unknown user");

        if (user.TotpEnabledAt is not null)
            throw new InvalidOperationException("TOTP is already enabled. Disable it first to re-enrol.");

        // Replace any unconfirmed in-flight secret. Since TotpEnabledAt is
        // still NULL, no live login is depending on the old draft.
        var secret = _totp.GenerateSecret();
        user.TotpSecret = secret;
        await _db.SaveChangesAsync(cancellationToken);

        var label = $"{user.Username}";
        return new TotpEnrolment(
            OtpAuthUri: _totp.OtpAuthUri(secret, "sverre.dev", label),
            Base32Secret: _totp.Base32Encode(secret));
    }

    public async Task ConfirmTotpEnrolmentAsync(Guid userId, string code, CancellationToken cancellationToken = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new AuthFailedException("Unknown user");

        if (user.TotpSecret is null)
            throw new InvalidOperationException("Start an enrolment first.");
        if (user.TotpEnabledAt is not null)
            throw new InvalidOperationException("TOTP is already enabled.");

        if (!_totp.Verify(user.TotpSecret, code))
            throw new AuthFailedException("Invalid TOTP code");

        user.TotpEnabledAt = DateTime.UtcNow;
        _audit.Record(userId, AuditKind.TotpEnabled);
        await _db.SaveChangesAsync(cancellationToken);
        FireSecurityAlert(user.Email, "Two-factor authentication enabled");
    }

    public async Task DisableTotpAsync(Guid userId, string code, CancellationToken cancellationToken = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new AuthFailedException("Unknown user");

        if (user.TotpEnabledAt is null || user.TotpSecret is null)
            throw new InvalidOperationException("TOTP is not enabled.");

        // Require a current code: an attacker with a hijacked access token
        // shouldn't be able to neuter the second factor in one click. Recovery
        // codes don't count here — disabling needs the live second factor.
        if (!_totp.Verify(user.TotpSecret, code))
            throw new AuthFailedException("Invalid TOTP code");

        user.TotpSecret = null;
        user.TotpEnabledAt = null;
        // Old recovery codes are useless without a TOTP secret; drop them so
        // a future re-enrolment starts from a clean sheet.
        await _db.RecoveryCodes
            .Where(r => r.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        _audit.Record(userId, AuditKind.TotpDisabled);
        await _db.SaveChangesAsync(cancellationToken);
        FireSecurityAlert(user.Email, "Two-factor authentication disabled");
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

    public async Task ChangePasswordAsync(Guid userId, string currentClientHash, string newClientHash, CancellationToken cancellationToken = default)
    {
        RejectIfLooksLikeRawPassword(currentClientHash);
        RejectIfLooksLikeRawPassword(newClientHash);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new AuthFailedException("Unknown user");

        var attempt = Argon2(HexToBytes(currentClientHash), user.PasswordSalt);
        if (!CryptographicOperations.FixedTimeEquals(attempt, user.PasswordHash))
            throw new AuthFailedException("Current password is incorrect");

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        user.PasswordHash = Argon2(HexToBytes(newClientHash), salt);
        user.PasswordSalt = salt;

        // Same as password-reset: changing credentials invalidates every
        // active session so any attacker with a captured token is out.
        await _db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTime.UtcNow), cancellationToken);

        _audit.Record(userId, AuditKind.PasswordChanged);
        await _db.SaveChangesAsync(cancellationToken);
        FireSecurityAlert(user.Email, "Password changed");
    }

    public async Task RevokeAllSessionsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await _db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTime.UtcNow), cancellationToken);
        _audit.Record(userId, AuditKind.SessionsRevoked);
        await _db.SaveChangesAsync(cancellationToken);

        var address = await _db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.Email)
            .FirstOrDefaultAsync(cancellationToken);
        if (address is not null) FireSecurityAlert(address, "All sessions revoked");
    }

    // Fire-and-forget security notification. Wrapped in a task that swallows
    // exceptions so an SMTP failure does not unwind the calling action — the
    // database state has already committed by the time we get here.
    private void FireSecurityAlert(string toEmail, string actionLabel, string? extraNote = null)
    {
        _ = Task.Run(async () =>
        {
            try { await _email.SendSecurityAlertAsync(toEmail, actionLabel, extraNote); }
            catch { /* logged by EmailService.SendAsync */ }
        });
    }

    public async Task RequestEmailChangeAsync(Guid userId, string newEmail, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newEmail) || !newEmail.Contains('@'))
            throw new InvalidOperationException("A valid email address is required");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new AuthFailedException("Unknown user");

        // No-op if the address is already on this account.
        if (string.Equals(user.Email, newEmail, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("That is already the address on this account");

        // Block obvious clashes early. The unique index on Users.Email is the
        // ultimate authority — but we don't want to send a confirmation link
        // that we know will never be applicable. Generic message: the existing
        // account holder shouldn't learn that their address is in use here.
        if (await _db.Users.AnyAsync(u => u.Email == newEmail, cancellationToken))
            throw new InvalidOperationException("Email change could not be requested. Try a different address.");

        var token = _jwt.CreateEmailChangeToken(userId, newEmail);
        await _email.SendEmailChangeAsync(newEmail, token);
    }

    public async Task<bool> ConfirmEmailChangeAsync(string jwtToken, CancellationToken cancellationToken = default)
    {
        var principal = _jwt.Validate(jwtToken, expectedPurpose: JwtPurpose.EmailChange);
        if (principal is null) return false;

        var sub = principal.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
        var newEmail = principal.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Email)?.Value;
        if (!Guid.TryParse(sub, out var userId) || string.IsNullOrWhiteSpace(newEmail)) return false;

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null) return false;

        // Re-check uniqueness at confirm time — somebody else may have grabbed
        // the address between request and click.
        if (await _db.Users.AnyAsync(u => u.Email == newEmail && u.Id != userId, cancellationToken))
            return false;

        var oldEmail = user.Email;
        user.Email = newEmail;
        // Token possession proves the user controls the new address; flip the
        // verified-at to now so the new address is treated as confirmed.
        user.EmailVerifiedAt = DateTime.UtcNow;

        // Email change is a sensitive credential update — revoke every active
        // session, same as a password change, so any attacker hopping in via
        // a stolen refresh token gets evicted.
        await _db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTime.UtcNow), cancellationToken);

        // Detail records the destination domain only — never the full address —
        // so a leaked DB doesn't leak the user's mailbox.
        _audit.Record(userId, AuditKind.EmailChanged, $"to *@{DomainOf(newEmail)} (from *@{DomainOf(oldEmail)})");
        await _db.SaveChangesAsync(cancellationToken);

        // Notify BOTH addresses: the new one knows the change happened, and
        // the *old* one finds out so an attacker can't silently hijack the
        // recovery channel without the legitimate owner getting a heads-up.
        FireSecurityAlert(newEmail, "Email changed", "This is now the address on file for the account.");
        FireSecurityAlert(oldEmail, "Email changed", $"The account email was changed to *@{DomainOf(newEmail)}. If this wasn't you, contact the controller immediately — your old address can no longer reset the account.");

        return true;
    }

    private static string DomainOf(string email)
    {
        var at = email.LastIndexOf('@');
        return at >= 0 && at < email.Length - 1 ? email[(at + 1)..] : "unknown";
    }

    public async Task ResendVerificationAsync(string email, CancellationToken cancellationToken = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);
        // Silent no-op: unknown email or already verified — don't leak which case.
        if (user is null || user.EmailVerifiedAt is not null) return;

        var token = _jwt.CreateEmailVerifyToken(user.Id, user.Email);
        user.EmailVerifySentAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await _email.SendVerificationAsync(user.Email, token);
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

    public async Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);
        if (user is null) return; // silent — don't leak whether the address has an account

        var token = _jwt.CreatePasswordResetToken(user.Id);
        await _email.SendPasswordResetAsync(user.Email, token);
    }

    public async Task ResetPasswordAsync(string jwtToken, string clientHashHex, CancellationToken cancellationToken = default)
    {
        RejectIfLooksLikeRawPassword(clientHashHex);

        var principal = _jwt.Validate(jwtToken, expectedPurpose: JwtPurpose.PasswordReset);
        if (principal is null) throw new AuthFailedException("Invalid or expired reset token");

        var sub = principal.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
        if (!Guid.TryParse(sub, out var userId)) throw new AuthFailedException("Reset token has no subject");

        var user = await _db.Users.FindAsync(new object?[] { userId }, cancellationToken)
            ?? throw new AuthFailedException("User not found");

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Argon2(HexToBytes(clientHashHex), salt);
        user.PasswordHash = hash;
        user.PasswordSalt = salt;

        // A password reset is the explicit "I lost control of this account" signal —
        // revoke every active refresh token so any session held by an attacker dies.
        await _db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTime.UtcNow), cancellationToken);

        // The email-reset flow is the only canonical way back into an account
        // when both the authenticator AND the recovery codes are lost. Clear
        // TOTP so a lost device doesn't become a permanent lockout. The user
        // re-enrols from /account on next login.
        var clearedTotp = false;
        if (user.TotpEnabledAt is not null || user.TotpSecret is not null)
        {
            user.TotpSecret = null;
            user.TotpEnabledAt = null;
            await _db.RecoveryCodes
                .Where(r => r.UserId == userId)
                .ExecuteDeleteAsync(cancellationToken);
            clearedTotp = true;
        }

        _audit.Record(userId, AuditKind.PasswordReset, clearedTotp ? "TOTP also cleared" : null);
        await _db.SaveChangesAsync(cancellationToken);

        // Heads-up to the address that received the reset link — confirms the
        // reset went through and gives the user a chance to react if their
        // mailbox is compromised.
        FireSecurityAlert(user.Email, "Password reset",
            clearedTotp ? "Two-factor authentication was also cleared as part of the reset." : null);
    }

    public async Task<bool> SeedAdminIfEmptyAsync(string username, string email, CancellationToken cancellationToken = default)
    {
        if (await _db.Users.AnyAsync(cancellationToken)) return false;

        // Apply the same shape rules as user-driven registration so the seed
        // can't quietly bypass them — e.g. a config like "Admin" would land
        // in the DB mixed-case and then never log in via the normalised path.
        username = UsernameNormalizer.NormaliseForRegister(username);

        // Mint a high-entropy password we will never log, return, or persist —
        // the only path back into this account is the password-reset flow,
        // which goes through the configured email address.
        var passwordBytes = RandomNumberGenerator.GetBytes(64);
        try
        {
            // Mirror what the client would send: SHA-256 of the password.
            var clientHash = SHA256.HashData(passwordBytes);
            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var hash = Argon2(clientHash, salt);

            _db.Users.Add(new User
            {
                Username = username,
                Email = email,
                PasswordHash = hash,
                PasswordSalt = salt,
                EmailVerifiedAt = DateTime.UtcNow, // pre-verified so reset works
                IsAdmin = true
            });
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        finally
        {
            // Explicitly zero the buffer so the random password isn't sitting
            // in memory waiting for the GC.
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
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
