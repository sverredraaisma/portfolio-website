using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PortfolioApi.Configuration;
using PortfolioApi.Services;

namespace PortfolioApi.Tests.Services;

public class PolicyServiceTests : IDisposable
{
    private readonly string _root;

    public PolicyServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "policy-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "Resources"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private FalconSigningService BuildSigner() => new(
        Options.Create(new SigningOptions { KeyPath = "keys" }),
        new TestEnv(_root),
        NullLogger<FalconSigningService>.Instance);

    private void WritePolicy(string contents) =>
        File.WriteAllText(Path.Combine(_root, "Resources", "privacy-policy.txt"), contents);

    [Fact]
    public void Privacy_returns_the_text_verbatim()
    {
        // Whitespace and line endings must round-trip — the signature attests
        // to *exact* bytes, so a normalising read would invalidate offline
        // verification by anyone who fetched the same file.
        var text = "Title\n\nLast-Updated: 2026-04-24\n\nBody line.\nSecond line.\n";
        WritePolicy(text);

        var sut = new PolicyService(BuildSigner(), new TestEnv(_root), NullLogger<PolicyService>.Instance);

        sut.Privacy.Text.Should().Be(text);
        sut.Privacy.LastUpdated.Should().Be("2026-04-24");
        sut.Privacy.Subject.Should().Be("privacy-policy");
    }

    [Fact]
    public void Privacy_signature_verifies_against_the_signing_service()
    {
        WritePolicy("Title\n\nLast-Updated: 2026-04-24\n\nBody.\n");
        var signer = BuildSigner();

        var sut = new PolicyService(signer, new TestEnv(_root), NullLogger<PolicyService>.Instance);

        var verified = signer.Verify(sut.Privacy.Text, sut.Privacy.SignatureBase64);
        verified.Valid.Should().BeTrue();
        verified.PublicKeyFingerprint.Should().Be(sut.Privacy.PublicKeyFingerprint);
    }

    [Fact]
    public void Privacy_snapshot_is_byte_stable_across_repeat_reads()
    {
        // Repeated calls must return the same signature bytes — the snapshot
        // a visitor saved last week has to compare equal to the snapshot a
        // visitor sees today, otherwise "I downloaded this on date X" claims
        // are unprovable.
        WritePolicy("Title\n\nLast-Updated: 2026-04-24\n\nBody.\n");

        var sut = new PolicyService(BuildSigner(), new TestEnv(_root), NullLogger<PolicyService>.Instance);

        sut.Privacy.SignatureBase64.Should().Be(sut.Privacy.SignatureBase64);
        ReferenceEquals(sut.Privacy, sut.Privacy).Should().BeTrue("snapshot is computed once and reused");
    }

    [Fact]
    public void Construction_throws_when_the_text_file_is_missing()
    {
        var act = () => new PolicyService(BuildSigner(), new TestEnv(_root), NullLogger<PolicyService>.Instance);

        act.Should().Throw<InvalidOperationException>().WithMessage("*not found*");
    }

    [Fact]
    public void Construction_throws_when_the_text_lacks_a_Last_Updated_line()
    {
        // The Last-Updated header is part of the signed bytes (the parser
        // doesn't strip it), but it's also load-bearing for the UI's "as of"
        // badge. A policy without it is a contract bug — fail fast at boot.
        WritePolicy("Title\n\nBody only — no header.\n");

        var act = () => new PolicyService(BuildSigner(), new TestEnv(_root), NullLogger<PolicyService>.Instance);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Last-Updated*");
    }

    [Fact]
    public void Privacy_publicKeyFingerprint_matches_the_signer()
    {
        WritePolicy("Title\n\nLast-Updated: 2026-04-24\n\nBody.\n");
        var signer = BuildSigner();

        var sut = new PolicyService(signer, new TestEnv(_root), NullLogger<PolicyService>.Instance);

        sut.Privacy.PublicKeyFingerprint.Should().Be(signer.PublicKeyFingerprint);
        sut.Privacy.Algorithm.Should().Be("falcon-512");
    }

    private sealed class TestEnv : IWebHostEnvironment
    {
        public TestEnv(string root) { ContentRootPath = root; WebRootPath = root; }
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "PortfolioApi.Tests";
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string WebRootPath { get; set; }
        public IFileProvider WebRootFileProvider { get; set; } = null!;
    }
}
