using System.Text.RegularExpressions;

namespace PortfolioApi.Services;

/// Snapshot returned by IPolicyService — the canonical text of a policy plus
/// a Falcon-512 signature attesting to those exact bytes. Visitors save this
/// bundle to prove what the site committed to on a given day.
public sealed record PolicySnapshot(
    string Subject,
    string Text,
    string LastUpdated,
    string Algorithm,
    string SignatureBase64,
    string PublicKeyBase64,
    string PublicKeyFingerprint);

public interface IPolicyService
{
    /// Read the canonical privacy policy and return a signed snapshot. The
    /// signature is computed once at startup and cached, so repeat visitors
    /// see byte-identical proof material — important for "I saved this on
    /// date X and verified it later" semantics.
    PolicySnapshot Privacy { get; }
}

/// Loads the canonical policy text from disk at startup, signs it once with
/// the site's Falcon-512 key, and serves the cached signed bundle. The text
/// must include a `Last-Updated: YYYY-MM-DD` line — the parser pulls it out
/// for the UI's "as of" badge but the line is still part of the signed bytes.
public sealed class PolicyService : IPolicyService
{
    private static readonly Regex LastUpdatedRx = new(
        @"^\s*Last-Updated:\s*(\d{4}-\d{2}-\d{2})\s*$",
        RegexOptions.Multiline | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public PolicySnapshot Privacy { get; }

    public PolicyService(ISigningService signer, IWebHostEnvironment env, ILogger<PolicyService> log)
    {
        var path = Path.Combine(env.ContentRootPath, "Resources", "privacy-policy.txt");
        if (!File.Exists(path))
            throw new InvalidOperationException(
                $"Canonical privacy policy text not found at {path}. The .csproj " +
                "should copy Resources/** next to the assembly.");

        // Read with the file's native bytes — no normalisation, no BOM strip,
        // no line-ending translation. The signed text must be exactly what
        // the file contains so an offline verifier can re-fetch and re-sign.
        var text = File.ReadAllText(path);
        var lastUpdated = ExtractLastUpdated(text)
            ?? throw new InvalidOperationException(
                "privacy-policy.txt missing a 'Last-Updated: YYYY-MM-DD' line");

        var signed = signer.Sign(text);

        Privacy = new PolicySnapshot(
            Subject: "privacy-policy",
            Text: text,
            LastUpdated: lastUpdated,
            Algorithm: signed.Algorithm,
            SignatureBase64: signed.SignatureBase64,
            PublicKeyBase64: signed.PublicKeyBase64,
            PublicKeyFingerprint: signed.PublicKeyFingerprint);

        log.LogInformation(
            "Signed privacy policy (last-updated {Date}, fingerprint {Fp})",
            lastUpdated, signed.PublicKeyFingerprint);
    }

    private static string? ExtractLastUpdated(string text)
    {
        var m = LastUpdatedRx.Match(text);
        return m.Success ? m.Groups[1].Value : null;
    }
}
