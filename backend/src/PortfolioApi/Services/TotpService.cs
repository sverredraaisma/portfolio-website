using System.Security.Cryptography;

namespace PortfolioApi.Services;

/// RFC 6238 Time-based One-Time Password generator/verifier. Implemented in
///-place rather than pulling another NuGet — the spec is short, the
/// surface is small, and it keeps the dependency graph audit-friendly.
public sealed class TotpService : ITotpService
{
    private const int SecretBytes = 20;        // 160 bits — RFC 4226 §4 recommendation
    private const int Digits = 6;
    private const int StepSeconds = 30;
    private const int VerifyWindow = 1;        // ± one 30-second step (~tolerance for clock skew)

    public byte[] GenerateSecret() => RandomNumberGenerator.GetBytes(SecretBytes);

    public string OtpAuthUri(byte[] secret, string issuer, string accountLabel)
    {
        // otpauth://totp/<issuer>:<account>?secret=<base32>&issuer=<issuer>&algorithm=SHA1&digits=6&period=30
        var iss = Uri.EscapeDataString(issuer);
        var acc = Uri.EscapeDataString(accountLabel);
        var b32 = Base32Encode(secret);
        return $"otpauth://totp/{iss}:{acc}?secret={b32}&issuer={iss}&algorithm=SHA1&digits=6&period=30";
    }

    public string Base32Encode(byte[] data)
    {
        // RFC 4648 base32 alphabet. No padding — most authenticator apps accept
        // both, but the unpadded form is the de-facto convention for otpauth URIs.
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        if (data.Length == 0) return "";

        var sb = new System.Text.StringBuilder(((data.Length * 8) + 4) / 5);
        int buffer = data[0];
        int next = 1;
        int bitsLeft = 8;
        while (bitsLeft > 0 || next < data.Length)
        {
            if (bitsLeft < 5)
            {
                if (next < data.Length)
                {
                    buffer = (buffer << 8) | data[next++];
                    bitsLeft += 8;
                }
                else
                {
                    var pad = 5 - bitsLeft;
                    buffer <<= pad;
                    bitsLeft += pad;
                }
            }
            int index = (buffer >> (bitsLeft - 5)) & 0x1F;
            bitsLeft -= 5;
            sb.Append(alphabet[index]);
        }
        return sb.ToString();
    }

    public bool Verify(byte[] secret, string code)
    {
        if (secret is null || secret.Length == 0) return false;
        if (string.IsNullOrWhiteSpace(code)) return false;
        // Strip a stray space or two BEFORE the length check — many apps
        // display "123 456" with the gap baked in.
        var trimmed = code.Replace(" ", "");
        if (trimmed.Length != Digits) return false;
        foreach (var ch in trimmed)
            if (ch < '0' || ch > '9') return false;

        var counter = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / StepSeconds;
        for (int delta = -VerifyWindow; delta <= VerifyWindow; delta++)
        {
            var expected = ComputeCode(secret, counter + delta);
            // FixedTimeEquals on the digit strings — the strings are short and
            // fixed-length, so this is constant time within the inner loop.
            if (FixedTimeEquals(expected, trimmed)) return true;
        }
        return false;
    }

    private static string ComputeCode(byte[] secret, long counter)
    {
        // RFC 6238 §4.2 / RFC 4226 §5.3
        var counterBytes = new byte[8];
        for (int i = 7; i >= 0; i--)
        {
            counterBytes[i] = (byte)(counter & 0xFF);
            counter >>= 8;
        }

        using var hmac = new HMACSHA1(secret);
        var hash = hmac.ComputeHash(counterBytes);

        var offset = hash[^1] & 0x0F;
        var binary =
              ((hash[offset]     & 0x7F) << 24)
            | ((hash[offset + 1] & 0xFF) << 16)
            | ((hash[offset + 2] & 0xFF) << 8)
            |  (hash[offset + 3] & 0xFF);

        var otp = binary % 1_000_000;
        return otp.ToString("D6");
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        var diff = 0;
        for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }
}
