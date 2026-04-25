using System.Text.Json;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PortfolioApi.Constants;
using PortfolioApi.Data;
using PortfolioApi.Models;

namespace PortfolioApi.Services;

public sealed class PasskeyService : IPasskeyService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan SessionTtl = TimeSpan.FromMinutes(5);

    private readonly AppDbContext _db;
    private readonly IFido2 _fido;
    private readonly IMemoryCache _cache;
    private readonly IAuditService _audit;

    public PasskeyService(AppDbContext db, IFido2 fido, IMemoryCache cache, IAuditService audit)
    {
        _db = db;
        _fido = fido;
        _cache = cache;
        _audit = audit;
    }

    public async Task<PasskeyRegistrationStart> StartRegistrationAsync(User user, CancellationToken cancellationToken = default)
    {
        var existing = await _db.Passkeys
            .Where(p => p.UserId == user.Id)
            .Select(p => new PublicKeyCredentialDescriptor(p.CredentialId))
            .ToListAsync(cancellationToken);

        var fidoUser = new Fido2User
        {
            Id = user.Id.ToByteArray(),
            Name = user.Username,
            DisplayName = user.Username
        };

        var options = _fido.RequestNewCredential(new RequestNewCredentialParams
        {
            User = fidoUser,
            ExcludeCredentials = existing,
            AuthenticatorSelection = new AuthenticatorSelection
            {
                ResidentKey = ResidentKeyRequirement.Required,
                UserVerification = UserVerificationRequirement.Preferred
            },
            AttestationPreference = AttestationConveyancePreference.None
        });

        var sessionId = NewSessionId();
        Stash($"reg:{sessionId}", options.ToJson());
        return new PasskeyRegistrationStart(options.ToJson(), sessionId);
    }

    public async Task<PasskeyDto> FinishRegistrationAsync(Guid userId, string sessionId, string attestationJson, string name, CancellationToken cancellationToken = default)
    {
        var optionsJson = Take($"reg:{sessionId}")
            ?? throw new AuthFailedException("Registration session expired");
        var options = CredentialCreateOptions.FromJson(optionsJson);
        var attestation = JsonSerializer.Deserialize<AuthenticatorAttestationRawResponse>(attestationJson, JsonOpts)
            ?? throw new InvalidOperationException("Malformed attestation response");

        var result = await _fido.MakeNewCredentialAsync(new MakeNewCredentialParams
        {
            AttestationResponse = attestation,
            OriginalOptions = options,
            IsCredentialIdUniqueToUserCallback = async (args, ct) =>
            {
                var clash = await _db.Passkeys.AnyAsync(p => p.CredentialId == args.CredentialId, ct);
                return !clash;
            }
        }, cancellationToken);

        var passkey = new Passkey
        {
            UserId = userId,
            CredentialId = result.Id,
            PublicKey = result.PublicKey,
            SignCount = result.SignCount,
            Aaguid = result.AaGuid,
            Name = SanitiseName(name)
        };
        _db.Passkeys.Add(passkey);
        _audit.Record(userId, AuditKind.PasskeyAdded, $"name={passkey.Name}");
        await _db.SaveChangesAsync(cancellationToken);

        return new PasskeyDto(passkey.Id, passkey.Name, passkey.CreatedAt, passkey.LastUsedAt);
    }

    public async Task<PasskeyAssertionStart> StartAssertionAsync(string? username, CancellationToken cancellationToken = default)
    {
        var allowed = new List<PublicKeyCredentialDescriptor>();
        // Permissive lookup: lowercases + validates shape so "ALICE" matches
        // the stored "alice" and obviously-bad input doesn't reach the DB.
        var key = UsernameNormalizer.NormaliseForLookup(username);
        if (key is not null)
        {
            allowed = await _db.Passkeys
                .Where(p => p.User!.Username == key)
                .Select(p => new PublicKeyCredentialDescriptor(p.CredentialId))
                .ToListAsync(cancellationToken);
        }

        var options = _fido.GetAssertionOptions(new GetAssertionOptionsParams
        {
            AllowedCredentials = allowed,
            UserVerification = UserVerificationRequirement.Preferred
        });

        var sessionId = NewSessionId();
        Stash($"login:{sessionId}", options.ToJson());
        return new PasskeyAssertionStart(options.ToJson(), sessionId);
    }

    public async Task<User> FinishAssertionAsync(string sessionId, string assertionJson, CancellationToken cancellationToken = default)
    {
        var optionsJson = Take($"login:{sessionId}")
            ?? throw new AuthFailedException("Login session expired");
        var options = AssertionOptions.FromJson(optionsJson);
        var assertion = JsonSerializer.Deserialize<AuthenticatorAssertionRawResponse>(assertionJson, JsonOpts)
            ?? throw new AuthFailedException("Malformed assertion response");

        // The browser returns the credential id as base64url; AssertionResponse
        // helpfully exposes RawId (byte[]) for the DB lookup.
        var rawId = assertion.RawId;
        var passkey = await _db.Passkeys
            .Include(p => p.User)
            .FirstOrDefaultAsync(p => p.CredentialId == rawId, cancellationToken)
            ?? throw new AuthFailedException("Unknown credential");

        if (passkey.User is null) throw new AuthFailedException("Credential has no user");

        var result = await _fido.MakeAssertionAsync(new MakeAssertionParams
        {
            AssertionResponse = assertion,
            OriginalOptions = options,
            StoredPublicKey = passkey.PublicKey,
            StoredSignatureCounter = passkey.SignCount,
            IsUserHandleOwnerOfCredentialIdCallback = (args, ct) =>
            {
                var matches = passkey.UserId.ToByteArray().AsSpan().SequenceEqual(args.UserHandle);
                return Task.FromResult(matches);
            }
        }, cancellationToken);

        passkey.SignCount = result.SignCount;
        passkey.LastUsedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return passkey.User;
    }

    public async Task<IReadOnlyList<PasskeyDto>> ListAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _db.Passkeys
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new PasskeyDto(p.Id, p.Name, p.CreatedAt, p.LastUsedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid userId, Guid passkeyId, CancellationToken cancellationToken = default)
    {
        var passkey = await _db.Passkeys.FirstOrDefaultAsync(p => p.Id == passkeyId && p.UserId == userId, cancellationToken)
            ?? throw new AuthFailedException("Passkey not found");
        _db.Passkeys.Remove(passkey);
        _audit.Record(userId, AuditKind.PasskeyRemoved, $"name={passkey.Name}");
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task RenameAsync(Guid userId, Guid passkeyId, string name, CancellationToken cancellationToken = default)
    {
        var passkey = await _db.Passkeys.FirstOrDefaultAsync(p => p.Id == passkeyId && p.UserId == userId, cancellationToken)
            ?? throw new AuthFailedException("Passkey not found");
        passkey.Name = SanitiseName(name);
        await _db.SaveChangesAsync(cancellationToken);
    }

    // --- helpers ---

    private static string NewSessionId() => Guid.NewGuid().ToString("N");

    private void Stash(string key, string json) => _cache.Set(key, json, SessionTtl);

    private string? Take(string key)
    {
        if (_cache.TryGetValue(key, out string? json))
        {
            _cache.Remove(key);
            return json;
        }
        return null;
    }

    private static string SanitiseName(string raw)
    {
        var trimmed = (raw ?? "").Trim();
        if (trimmed.Length == 0) return $"Passkey added {DateTime.UtcNow:yyyy-MM-dd}";
        if (trimmed.Length > 64) trimmed = trimmed[..64];
        return trimmed;
    }
}
