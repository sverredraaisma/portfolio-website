namespace PortfolioApi.Services;

public sealed record SignedStatement(
    string Algorithm,
    string Statement,
    string SignatureBase64,
    string PublicKeyBase64,
    string PublicKeyFingerprint,
    DateTime SignedAt);

public sealed record VerificationResult(bool Valid, string PublicKeyFingerprint);

/// Signs and verifies arbitrary text with the website's long-lived Falcon-512
/// keypair. The keypair is generated on first use and persisted to disk so
/// signatures survive container restarts.
public interface ISigningService
{
    string Algorithm { get; }

    /// Raw public-key bytes (Falcon-512 — 897 bytes including the parameter
    /// byte). Use `Convert.ToBase64String` for transport.
    byte[] PublicKey { get; }

    /// SHA-256 over the encoded public key, lowercase hex. Stable identifier
    /// for the key — useful for verifiers who want to pin a specific key.
    string PublicKeyFingerprint { get; }

    /// Sign the supplied statement. Returns the full envelope so the caller
    /// can publish or persist it.
    SignedStatement Sign(string statement);

    /// Verify a statement+signature pair. If <paramref name="publicKeyBase64"/>
    /// is provided, the verification uses that key instead of the website's
    /// own — handy for verifying historical statements after a key rotation.
    VerificationResult Verify(string statement, string signatureBase64, string? publicKeyBase64 = null);
}
