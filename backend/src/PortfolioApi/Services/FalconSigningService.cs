using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Org.BouncyCastle.Pqc.Crypto.Falcon;
using Org.BouncyCastle.Pqc.Crypto.Utilities;
using Org.BouncyCastle.Security;
using PortfolioApi.Configuration;

namespace PortfolioApi.Services;

/// Falcon-512 signer (NIST PQC). The key material is generated on first boot
/// and persisted to disk; subsequent boots reuse the same key so previously
/// published signatures keep verifying.
///
/// FalconSigner is not thread-safe (it is initialised with a key per-call),
/// so each Sign/Verify constructs its own signer over the cached key bytes.
public sealed class FalconSigningService : ISigningService
{
    public string Algorithm => "falcon-512";

    private static readonly FalconParameters Params = FalconParameters.falcon_512;

    private readonly FalconPublicKeyParameters _public;
    private readonly FalconPrivateKeyParameters _private;
    private readonly int _maxStatementBytes;
    private readonly ILogger<FalconSigningService> _log;

    public byte[] PublicKey { get; }
    public string PublicKeyFingerprint { get; }

    public FalconSigningService(
        IOptions<SigningOptions> opt,
        IWebHostEnvironment env,
        ILogger<FalconSigningService> log)
    {
        _log = log;
        _maxStatementBytes = opt.Value.MaxStatementBytes;

        var keyDir = Path.Combine(env.ContentRootPath, opt.Value.KeyPath);
        Directory.CreateDirectory(keyDir);
        var pubPath = Path.Combine(keyDir, "falcon.pub");
        var privPath = Path.Combine(keyDir, "falcon.priv");

        if (File.Exists(pubPath) && File.Exists(privPath))
        {
            // Stored as PKCS#8 / SubjectPublicKeyInfo so the on-disk format is
            // structured and version-stable (raw .GetEncoded() bytes changed
            // shape between BouncyCastle 2.4 and 2.6).
            _public = (FalconPublicKeyParameters)PqcPublicKeyFactory.CreateKey(File.ReadAllBytes(pubPath));
            _private = (FalconPrivateKeyParameters)PqcPrivateKeyFactory.CreateKey(File.ReadAllBytes(privPath));
        }
        else
        {
            // First boot — mint a fresh keypair and persist it. The private key
            // file is written with 0600 on POSIX so it isn't world-readable.
            var gen = new FalconKeyPairGenerator();
            gen.Init(new FalconKeyGenerationParameters(new SecureRandom(), Params));
            var kp = gen.GenerateKeyPair();
            _public = (FalconPublicKeyParameters)kp.Public;
            _private = (FalconPrivateKeyParameters)kp.Private;

            File.WriteAllBytes(pubPath, PqcSubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(_public).GetEncoded());
            File.WriteAllBytes(privPath, PqcPrivateKeyInfoFactory.CreatePrivateKeyInfo(_private).GetEncoded());
            TryRestrictPermissions(privPath);

            log.LogInformation("Generated Falcon-512 signing keypair at {Path}", keyDir);
        }

        // Raw key bytes for transport. The structured PKCS#8/SPKI files on
        // disk are an internal detail; the wire format is the bare public key.
        PublicKey = _public.GetEncoded();
        PublicKeyFingerprint = Convert.ToHexString(SHA256.HashData(PublicKey)).ToLowerInvariant();
    }

    public SignedStatement Sign(string statement)
    {
        EnsureBounded(statement);
        var bytes = Encoding.UTF8.GetBytes(statement);

        var signer = new FalconSigner();
        signer.Init(true, _private);
        var signature = signer.GenerateSignature(bytes);

        return new SignedStatement(
            Algorithm: Algorithm,
            Statement: statement,
            SignatureBase64: Convert.ToBase64String(signature),
            PublicKeyBase64: Convert.ToBase64String(PublicKey),
            PublicKeyFingerprint: PublicKeyFingerprint,
            SignedAt: DateTime.UtcNow);
    }

    public VerificationResult Verify(string statement, string signatureBase64, string? publicKeyBase64 = null)
    {
        EnsureBounded(statement);

        byte[] sig;
        try { sig = Convert.FromBase64String(signatureBase64); }
        catch (FormatException) { return new VerificationResult(false, ""); }

        FalconPublicKeyParameters key;
        string fingerprint;
        if (publicKeyBase64 is null)
        {
            key = _public;
            fingerprint = PublicKeyFingerprint;
        }
        else
        {
            byte[] pub;
            try { pub = Convert.FromBase64String(publicKeyBase64); }
            catch (FormatException) { return new VerificationResult(false, ""); }
            try { key = new FalconPublicKeyParameters(Params, pub); }
            catch { return new VerificationResult(false, ""); }
            fingerprint = Convert.ToHexString(SHA256.HashData(pub)).ToLowerInvariant();
        }

        var verifier = new FalconSigner();
        verifier.Init(false, key);
        bool ok;
        try { ok = verifier.VerifySignature(Encoding.UTF8.GetBytes(statement), sig); }
        catch { ok = false; }

        return new VerificationResult(ok, fingerprint);
    }

    private void EnsureBounded(string statement)
    {
        if (statement is null) throw new InvalidOperationException("statement required");
        // Encoding.UTF8.GetByteCount is O(n) but avoids allocating the full
        // byte buffer until we know the size is acceptable.
        if (Encoding.UTF8.GetByteCount(statement) > _maxStatementBytes)
            throw new InvalidOperationException($"statement exceeds {_maxStatementBytes} bytes");
    }

    private static void TryRestrictPermissions(string path)
    {
        try
        {
            // POSIX-only; on Windows containers we trust the file ACL inherited
            // from the parent directory.
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch
        {
            // Best-effort. Don't fail boot just because chmod didn't take.
        }
    }
}
