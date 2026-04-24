using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PortfolioApi.Configuration;
using PortfolioApi.Services;

namespace PortfolioApi.Tests.Services;

public class FalconSigningServiceTests : IDisposable
{
    private readonly string _root;

    public FalconSigningServiceTests()
    {
        // Each test gets its own temp content root so we exercise both the
        // "fresh keypair" and the "reload from disk" code paths.
        _root = Path.Combine(Path.GetTempPath(), "falcon-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private FalconSigningService Build()
    {
        var env = new TestEnv(_root);
        var opts = Options.Create(new SigningOptions { KeyPath = "keys" });
        return new FalconSigningService(opts, env, NullLogger<FalconSigningService>.Instance);
    }

    [Fact]
    public void First_use_generates_and_persists_a_keypair()
    {
        _ = Build();

        File.Exists(Path.Combine(_root, "keys", "falcon.pub")).Should().BeTrue();
        File.Exists(Path.Combine(_root, "keys", "falcon.priv")).Should().BeTrue();
    }

    [Fact]
    public void Sign_then_Verify_is_a_round_trip_for_the_same_service_instance()
    {
        var sut = Build();
        var envelope = sut.Sign("hello world");

        var result = sut.Verify(envelope.Statement, envelope.SignatureBase64);

        result.Valid.Should().BeTrue();
    }

    [Fact]
    public void Subsequent_instances_reload_the_persisted_key_so_signatures_keep_verifying()
    {
        var first = Build();
        var envelope = first.Sign("treaty");

        // Reload — same disk root, fresh service instance.
        var reloaded = Build();

        reloaded.Verify(envelope.Statement, envelope.SignatureBase64).Valid.Should().BeTrue();
        reloaded.PublicKeyFingerprint.Should().Be(first.PublicKeyFingerprint);
    }

    [Fact]
    public void Verify_returns_false_when_the_statement_was_tampered_with()
    {
        var sut = Build();
        var envelope = sut.Sign("the original statement");

        sut.Verify("a different statement", envelope.SignatureBase64).Valid.Should().BeFalse();
    }

    [Fact]
    public void Verify_against_an_explicit_public_key_uses_that_keys_fingerprint()
    {
        var sut = Build();
        var envelope = sut.Sign("anchored");

        var result = sut.Verify(envelope.Statement, envelope.SignatureBase64, envelope.PublicKeyBase64);

        result.Valid.Should().BeTrue();
        result.PublicKeyFingerprint.Should().Be(envelope.PublicKeyFingerprint);
    }

    [Fact]
    public void Verify_returns_false_for_a_garbage_signature_without_throwing()
    {
        var sut = Build();

        var result = sut.Verify("hi", "not-base64!");

        result.Valid.Should().BeFalse();
    }

    [Fact]
    public void Sign_rejects_an_oversized_statement()
    {
        var sut = new FalconSigningService(
            Options.Create(new SigningOptions { KeyPath = "keys", MaxStatementBytes = 16 }),
            new TestEnv(_root),
            NullLogger<FalconSigningService>.Instance);

        var act = () => sut.Sign(new string('x', 100));

        act.Should().Throw<InvalidOperationException>();
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
