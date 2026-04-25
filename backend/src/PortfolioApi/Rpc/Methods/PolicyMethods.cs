using PortfolioApi.Services;

namespace PortfolioApi.Rpc.Methods;

/// Wire shape for a signed policy snapshot. Field names mirror PolicySnapshot
/// — the Web JsonSerializer naming policy is camelCase so the client sees
/// `text`, `lastUpdated`, `signatureBase64`, etc. The shape is stable so a
/// JSON file a visitor saved a year ago still verifies in the future.
public sealed record PolicySnapshotDto(
    string Subject,
    string Text,
    string LastUpdated,
    string Algorithm,
    string SignatureBase64,
    string PublicKeyBase64,
    string PublicKeyFingerprint);

public class PolicyMethods
{
    private readonly IPolicyService _policy;

    public PolicyMethods(IPolicyService policy) => _policy = policy;

    /// Public: return the signed canonical privacy policy. The visitor saves
    /// this bundle as proof of what the site committed to on a given date.
    public Task<PolicySnapshotDto> Privacy(RpcContext ctx)
    {
        var s = _policy.Privacy;
        return Task.FromResult(new PolicySnapshotDto(
            s.Subject, s.Text, s.LastUpdated, s.Algorithm,
            s.SignatureBase64, s.PublicKeyBase64, s.PublicKeyFingerprint));
    }
}
