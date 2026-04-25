using FluentAssertions;
using PortfolioApi.Services;

namespace PortfolioApi.Tests.Services;

public class UsernameNormalizerTests
{
    // ---- NormaliseForRegister --------------------------------------------

    [Theory]
    [InlineData("alice")]
    [InlineData("alice_99")]
    [InlineData("a-b-c")]
    [InlineData("abc")]
    [InlineData("a1234567890123456789012345678901")] // 32 chars, the upper bound
    public void Register_accepts_well_formed_lowercase_usernames(string input)
    {
        UsernameNormalizer.NormaliseForRegister(input).Should().Be(input);
    }

    [Fact]
    public void Register_trims_surrounding_whitespace_then_validates()
    {
        UsernameNormalizer.NormaliseForRegister("  alice  ").Should().Be("alice");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Register_rejects_blank_input(string input)
    {
        var act = () => UsernameNormalizer.NormaliseForRegister(input);
        act.Should().Throw<InvalidOperationException>().WithMessage("*required*");
    }

    [Theory]
    [InlineData("ab")]                                    // too short
    [InlineData("a12345678901234567890123456789012")]     // 33 chars
    public void Register_rejects_lengths_outside_the_3_to_32_window(string input)
    {
        var act = () => UsernameNormalizer.NormaliseForRegister(input);
        act.Should().Throw<InvalidOperationException>().WithMessage("*3-32*");
    }

    [Fact]
    public void Register_rejects_mixed_case_explicitly_to_avoid_silent_renames()
    {
        var act = () => UsernameNormalizer.NormaliseForRegister("Alice");
        act.Should().Throw<InvalidOperationException>().WithMessage("*lowercase*");
    }

    [Theory]
    [InlineData("has spaces")]
    [InlineData("dot.in.middle")]
    [InlineData("uniçode")]
    [InlineData("emoji😀")]
    [InlineData("_leading")]
    [InlineData("trailing-")]
    [InlineData("-leading-dash")]
    public void Register_rejects_disallowed_characters_or_boundary_punctuation(string input)
    {
        var act = () => UsernameNormalizer.NormaliseForRegister(input);
        act.Should().Throw<InvalidOperationException>();
    }

    // ---- NormaliseForLookup ----------------------------------------------

    [Fact]
    public void Lookup_lowercases_so_capitalised_input_finds_the_canonical_form()
    {
        UsernameNormalizer.NormaliseForLookup("ALICE").Should().Be("alice");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("ab")]
    [InlineData("a12345678901234567890123456789012")]
    [InlineData("has spaces")]
    [InlineData("_leading")]
    public void Lookup_returns_null_for_anything_that_could_not_be_a_stored_username(string? input)
    {
        UsernameNormalizer.NormaliseForLookup(input).Should().BeNull();
    }
}
