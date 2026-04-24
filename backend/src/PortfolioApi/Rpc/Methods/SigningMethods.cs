using PortfolioApi.Services;

namespace PortfolioApi.Rpc.Methods;

// ---- Param records ---------------------------------------------------------

public sealed record SignStatementParams
{
    public required string Statement { get; init; }
}

public sealed record VerifyStatementParams
{
    public required string Statement { get; init; }
    public required string SignatureBase64 { get; init; }
    /// Optional. When omitted the website's own current public key is used.
    public string? PublicKeyBase64 { get; init; }
}

// ---- Response records ------------------------------------------------------

public sealed record PublicKeyDto(string Algorithm, string PublicKeyBase64, string Fingerprint);

public sealed record VerifyResultDto(bool Valid, string Fingerprint);

public class SigningMethods
{
    private readonly ISigningService _signer;

    public SigningMethods(ISigningService signer) => _signer = signer;

    public Task<PublicKeyDto> PublicKey(RpcContext ctx) =>
        Task.FromResult(new PublicKeyDto(
            _signer.Algorithm,
            Convert.ToBase64String(_signer.PublicKey),
            _signer.PublicKeyFingerprint));

    /// Admin-only: signs the statement with the website's private key.
    public Task<SignedStatement> Sign(SignStatementParams p, RpcContext ctx)
    {
        ctx.RequireAdmin();
        return Task.FromResult(_signer.Sign(p.Statement));
    }

    /// Public: verifies a statement+signature pair. Anyone can call this.
    public Task<VerifyResultDto> Verify(VerifyStatementParams p, RpcContext ctx)
    {
        var r = _signer.Verify(p.Statement, p.SignatureBase64, p.PublicKeyBase64);
        return Task.FromResult(new VerifyResultDto(r.Valid, r.PublicKeyFingerprint));
    }
}
