using FluentAssertions;
using PortfolioApi.Services;

namespace PortfolioApi.Tests.Services;

public class TotpServiceTests
{
    private readonly TotpService _sut = new();

    [Fact]
    public void GenerateSecret_returns_20_random_bytes()
    {
        var a = _sut.GenerateSecret();
        var b = _sut.GenerateSecret();

        a.Should().HaveCount(20);
        b.Should().HaveCount(20);
        a.Should().NotEqual(b, "two consecutive secrets must differ");
    }

    [Fact]
    public void Base32Encode_round_trip_through_otpauth_uri_uses_RFC4648_alphabet()
    {
        var secret = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x00 };

        var encoded = _sut.Base32Encode(secret);

        // Known good encoding for these bytes (verified against an external TOTP tool).
        encoded.Should().Be("32W353YA");
    }

    [Fact]
    public void OtpAuthUri_embeds_issuer_account_and_base32_secret()
    {
        var secret = _sut.GenerateSecret();

        var uri = _sut.OtpAuthUri(secret, "sverre.dev", "alice");

        uri.Should().StartWith("otpauth://totp/sverre.dev:alice?");
        uri.Should().Contain("secret=" + _sut.Base32Encode(secret));
        uri.Should().Contain("issuer=sverre.dev");
        uri.Should().Contain("algorithm=SHA1");
        uri.Should().Contain("digits=6");
        uri.Should().Contain("period=30");
    }

    [Fact]
    public void Verify_accepts_a_freshly_generated_code()
    {
        // We don't have a code-generator on the public surface, but the
        // service trivially accepts its own output: we sign with a known
        // secret and call Verify against the value we computed externally.
        // Easier: sign once via the service's internals via reflection? No —
        // just generate a secret, verify a wrong code returns false, and
        // verify that the *exact* TOTP for the current step is accepted.
        var secret = _sut.GenerateSecret();
        var code = ComputeTotp(secret, DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);

        _sut.Verify(secret, code).Should().BeTrue();
    }

    [Fact]
    public void Verify_accepts_codes_from_the_previous_30s_step_within_drift_tolerance()
    {
        var secret = _sut.GenerateSecret();
        var prevStep = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30) - 1;
        var code = ComputeTotp(secret, prevStep);

        _sut.Verify(secret, code).Should().BeTrue();
    }

    [Fact]
    public void Verify_rejects_codes_outside_the_drift_window()
    {
        var secret = _sut.GenerateSecret();
        var farFuture = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30) + 5;
        var code = ComputeTotp(secret, farFuture);

        _sut.Verify(secret, code).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]   // too short
    [InlineData("1234567")] // too long
    [InlineData("12345a")]  // non-digit
    [InlineData(null)]
    public void Verify_rejects_malformed_codes_without_throwing(string? input)
    {
        var secret = _sut.GenerateSecret();

        _sut.Verify(secret, input!).Should().BeFalse();
    }

    [Fact]
    public void Verify_strips_internal_spaces_so_displayed_groupings_are_accepted()
    {
        var secret = _sut.GenerateSecret();
        var code = ComputeTotp(secret, DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
        var spaced = code[..3] + " " + code[3..];

        _sut.Verify(secret, spaced).Should().BeTrue();
    }

    // Mirrors RFC 6238 / 4226 §5.3 so the tests are self-contained — if the
    // production helper drifts from spec we'll see it as a verify failure.
    private static string ComputeTotp(byte[] secret, long counter)
    {
        var counterBytes = new byte[8];
        for (int i = 7; i >= 0; i--) { counterBytes[i] = (byte)(counter & 0xFF); counter >>= 8; }
        using var hmac = new System.Security.Cryptography.HMACSHA1(secret);
        var hash = hmac.ComputeHash(counterBytes);
        var offset = hash[^1] & 0x0F;
        var binary =
              ((hash[offset]     & 0x7F) << 24)
            | ((hash[offset + 1] & 0xFF) << 16)
            | ((hash[offset + 2] & 0xFF) << 8)
            |  (hash[offset + 3] & 0xFF);
        return (binary % 1_000_000).ToString("D6");
    }
}
