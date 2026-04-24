using PortfolioApi.Models;

namespace PortfolioApi.Services;

public sealed record PasskeyRegistrationStart(string OptionsJson, string SessionId);
public sealed record PasskeyAssertionStart(string OptionsJson, string SessionId);

public sealed record PasskeyDto(Guid Id, string Name, DateTime CreatedAt, DateTime? LastUsedAt);

/// Wraps Fido2NetLib for our two flows: registering a new passkey on an
/// existing account, and signing in with one. The challenge issued by Start
/// is persisted in an in-memory cache keyed by SessionId so Finish can
/// recover the original options server-side rather than trusting the client
/// to round-trip them.
public interface IPasskeyService
{
    /// Registration step 1: returns CredentialCreateOptions JSON the browser
    /// hands to navigator.credentials.create().
    Task<PasskeyRegistrationStart> StartRegistrationAsync(User user, CancellationToken cancellationToken = default);

    /// Registration step 2: validates the attestation, persists the new
    /// Passkey row. <paramref name="name"/> is the user-supplied label.
    Task<PasskeyDto> FinishRegistrationAsync(Guid userId, string sessionId, string attestationJson, string name, CancellationToken cancellationToken = default);

    /// Login step 1: returns AssertionOptions JSON. When username is null the
    /// flow is "discoverable credential" — the browser picks the credential
    /// and returns the userHandle so we can identify the account.
    Task<PasskeyAssertionStart> StartAssertionAsync(string? username, CancellationToken cancellationToken = default);

    /// Login step 2: validates the assertion, returns the matched User.
    Task<User> FinishAssertionAsync(string sessionId, string assertionJson, CancellationToken cancellationToken = default);

    /// List the user's enrolled passkeys (no key material).
    Task<IReadOnlyList<PasskeyDto>> ListAsync(Guid userId, CancellationToken cancellationToken = default);

    /// Delete one of the user's passkeys.
    Task DeleteAsync(Guid userId, Guid passkeyId, CancellationToken cancellationToken = default);

    /// Rename a passkey.
    Task RenameAsync(Guid userId, Guid passkeyId, string name, CancellationToken cancellationToken = default);
}
