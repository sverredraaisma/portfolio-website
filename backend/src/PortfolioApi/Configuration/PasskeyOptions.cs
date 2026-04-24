namespace PortfolioApi.Configuration;

/// WebAuthn / FIDO2 relying-party configuration. The RpId must match the
/// public origin's registrable domain (no scheme, no port). For dev we
/// default to "localhost"; for production set Passkey:RpId to the host.
///
/// Origins are the full origin strings the browser uses (scheme + host
/// + port). Multiple are accepted so the same backend can serve dev (3000)
/// and prod (80/443) without a code change.
public sealed class PasskeyOptions
{
    public const string Section = "Passkey";

    public string RpId { get; set; } = "localhost";
    public string RpName { get; set; } = "sverre.dev";
    public string[] Origins { get; set; } =
    {
        "http://localhost",
        "http://localhost:3000",
        "http://localhost:5080"
    };
}
