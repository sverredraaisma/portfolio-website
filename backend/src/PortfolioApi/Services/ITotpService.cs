namespace PortfolioApi.Services;

public interface ITotpService
{
    /// Generate a fresh 20-byte HMAC-SHA1 secret. Caller persists it (initially
    /// as "pending"); same length recommended by RFC 4226 / 6238.
    byte[] GenerateSecret();

    /// Builds the otpauth:// URI that authenticator apps consume. Encodes the
    /// secret as base32 (RFC 4648), defaults to 6 digits / 30s / SHA1.
    string OtpAuthUri(byte[] secret, string issuer, string accountLabel);

    /// Base32-encode the secret so the user can type it in by hand if their
    /// authenticator app doesn't read QR codes.
    string Base32Encode(byte[] secret);

    /// Verify a 6-digit code with ±1 step tolerance (acceptable clock drift).
    /// Returns false on malformed input — the caller does not need to validate.
    bool Verify(byte[] secret, string code);
}
