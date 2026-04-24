using FluentAssertions;
using Microsoft.Extensions.Options;
using PortfolioApi.Configuration;
using PortfolioApi.Constants;
using PortfolioApi.Services;
using System.IdentityModel.Tokens.Jwt;

namespace PortfolioApi.Tests.Services;

public class JwtServiceTests
{
    private static JwtService Build(JwtOptions? overrides = null)
    {
        var opt = overrides ?? new JwtOptions
        {
            Issuer = "test-issuer",
            Audience = "test-audience",
            // 32+ bytes — JwtOptions enforces this at boot via DataAnnotations,
            // but for tests we just have to provide enough material.
            Key = "this-is-a-test-key-with-more-than-32-chars-please",
            AccessTokenMinutes = 15,
            RefreshTokenDays = 30,
            EmailVerifyHours = 24,
            PasswordResetHours = 1
        };
        return new JwtService(Options.Create(opt));
    }

    [Fact]
    public void Access_token_round_trips_through_validate_with_correct_purpose()
    {
        var sut = Build();
        var userId = Guid.NewGuid();

        var token = sut.CreateAccessToken(userId, "alice", isAdmin: false);
        var principal = sut.Validate(token, JwtPurpose.Access);

        principal.Should().NotBeNull();
        principal!.FindFirst(JwtRegisteredClaimNames.Sub)!.Value.Should().Be(userId.ToString());
        principal.FindFirst("username")!.Value.Should().Be("alice");
        principal.FindFirst("admin")!.Value.Should().Be("false");
    }

    [Fact]
    public void Wrong_purpose_returns_null_so_a_reset_token_cant_replay_as_an_access_token()
    {
        var sut = Build();
        var resetToken = sut.CreatePasswordResetToken(Guid.NewGuid());

        sut.Validate(resetToken, JwtPurpose.Access).Should().BeNull();
    }

    [Fact]
    public void EmailChange_token_carries_both_userId_and_new_email()
    {
        var sut = Build();
        var userId = Guid.NewGuid();

        var token = sut.CreateEmailChangeToken(userId, "new@example.com");
        var principal = sut.Validate(token, JwtPurpose.EmailChange);

        principal.Should().NotBeNull();
        principal!.FindFirst(JwtRegisteredClaimNames.Sub)!.Value.Should().Be(userId.ToString());
        principal.FindFirst(JwtRegisteredClaimNames.Email)!.Value.Should().Be("new@example.com");
    }

    [Fact]
    public void TotpChallenge_token_is_short_lived()
    {
        var sut = Build();
        var token = sut.CreateTotpChallengeToken(Guid.NewGuid());

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        // ValidFrom defaults to DateTime.MinValue when no nbf claim is set,
        // so we measure the TTL from now rather than from the token's "from".
        var ttl = jwt.ValidTo - DateTime.UtcNow;
        ttl.Should().BeLessThan(TimeSpan.FromMinutes(10));
        ttl.Should().BeGreaterThan(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void Validate_returns_null_for_a_garbage_token()
    {
        var sut = Build();

        sut.Validate("not-a-jwt", JwtPurpose.Access).Should().BeNull();
    }
}
