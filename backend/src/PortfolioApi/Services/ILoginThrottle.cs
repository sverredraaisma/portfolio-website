namespace PortfolioApi.Services;

/// Per-username brute-force throttle. The IP-based limiter on the HTTP
/// pipeline catches volumetric attacks; this catches credential-stuffing
/// where an attacker spreads attempts across many IPs against one account.
///
/// Stored in process memory — fine for single-instance deployments. If the
/// app ever scales horizontally, swap for a Redis-backed implementation.
public interface ILoginThrottle
{
    /// Throws AuthFailedException if the username is currently locked out.
    /// Call before doing any password work — the lockout response should
    /// arrive in the same time window as a normal login failure (no slow
    /// path to detect with timing).
    void EnsureNotLocked(string username);

    /// Record a failed attempt against this username. Call regardless of
    /// whether the username exists — locking unknown usernames doesn't leak
    /// existence and keeps the attacker from probing one name fast.
    void RecordFailure(string username);

    /// Clear the failure counter on a successful login.
    void Clear(string username);
}
