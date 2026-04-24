namespace PortfolioApi.Configuration;

/// Strongly-typed binding for the "Signing" configuration section.
/// KeyPath is resolved against ContentRootPath by FalconSigningService and is
/// where the Falcon-512 keypair is generated on first boot. The directory must
/// not be served by the static-files middleware — see Program.cs.
public sealed class SigningOptions
{
    public const string Section = "Signing";

    public string KeyPath { get; set; } = "keys";
    /// Maximum length of a statement that callers can submit for signing or
    /// verification. Bounded so a single request can't pin large amounts of
    /// memory or generate enormous signatures.
    public int MaxStatementBytes { get; set; } = 64 * 1024;
}
