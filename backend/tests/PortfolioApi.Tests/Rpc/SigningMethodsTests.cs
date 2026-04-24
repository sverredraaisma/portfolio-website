using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PortfolioApi.Configuration;
using PortfolioApi.Rpc.Methods;
using PortfolioApi.Services;
using PortfolioApi.Tests.Infrastructure;

namespace PortfolioApi.Tests.Rpc;

public class SigningMethodsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "signing-tests-" + Guid.NewGuid().ToString("N"));

    public SigningMethodsTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    private SigningMethods Build()
    {
        var signer = new FalconSigningService(
            Options.Create(new SigningOptions { KeyPath = "keys" }),
            new TestEnv(_root),
            NullLogger<FalconSigningService>.Instance);
        return new SigningMethods(signer);
    }

    [Fact]
    public async Task PublicKey_is_publicly_accessible_and_returns_the_algorithm_name()
    {
        var sut = Build();

        var dto = await sut.PublicKey(TestRpcContext.Anonymous());

        dto.Algorithm.Should().Be("falcon-512");
        dto.PublicKeyBase64.Should().NotBeNullOrEmpty();
        dto.Fingerprint.Should().HaveLength(64, "SHA-256 hex");
    }

    [Fact]
    public async Task Sign_requires_admin()
    {
        var sut = Build();

        var act = async () => await sut.Sign(
            new SignStatementParams { Statement = "treaty" },
            TestRpcContext.User(Guid.NewGuid()));

        await act.Should().ThrowAsync<AuthFailedException>();
    }

    [Fact]
    public async Task Sign_round_trips_through_Verify()
    {
        var sut = Build();
        var signed = await sut.Sign(new SignStatementParams { Statement = "anchored" }, TestRpcContext.Admin(Guid.NewGuid()));

        var verified = await sut.Verify(new VerifyStatementParams
        {
            Statement = "anchored",
            SignatureBase64 = signed.SignatureBase64
        }, TestRpcContext.Anonymous());

        verified.Valid.Should().BeTrue();
        verified.Fingerprint.Should().Be(signed.PublicKeyFingerprint);
    }

    [Fact]
    public async Task Verify_returns_false_for_a_tampered_statement_without_throwing()
    {
        var sut = Build();
        var signed = await sut.Sign(new SignStatementParams { Statement = "the original" }, TestRpcContext.Admin(Guid.NewGuid()));

        var verified = await sut.Verify(new VerifyStatementParams
        {
            Statement = "tampered",
            SignatureBase64 = signed.SignatureBase64
        }, TestRpcContext.Anonymous());

        verified.Valid.Should().BeFalse();
    }

    [Fact]
    public async Task Verify_against_an_explicit_public_key_uses_that_keys_fingerprint()
    {
        var sut = Build();
        var signed = await sut.Sign(new SignStatementParams { Statement = "x" }, TestRpcContext.Admin(Guid.NewGuid()));

        var verified = await sut.Verify(new VerifyStatementParams
        {
            Statement = "x",
            SignatureBase64 = signed.SignatureBase64,
            PublicKeyBase64 = signed.PublicKeyBase64
        }, TestRpcContext.Anonymous());

        verified.Valid.Should().BeTrue();
        verified.Fingerprint.Should().Be(signed.PublicKeyFingerprint);
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
