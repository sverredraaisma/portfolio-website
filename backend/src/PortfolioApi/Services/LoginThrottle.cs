using System.Collections.Concurrent;

namespace PortfolioApi.Services;

public sealed class LoginThrottle : ILoginThrottle
{
    // Sliding-window threshold. With Argon2id taking ~100ms per attempt the
    // attacker is already CPU-bottlenecked, but capping attempts/min still
    // dramatically narrows the search space.
    private const int MaxFailuresInWindow = 5;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    // Cap the dictionary so an attacker can't exhaust memory by spraying
    // unique usernames. When we hit the cap we drop the oldest stale entries.
    private const int MaxEntries = 10_000;

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    private sealed class Entry
    {
        public int Failures;
        public DateTime FirstFailureUtc;
    }

    public void EnsureNotLocked(string username)
    {
        var key = Normalise(username);
        if (!_entries.TryGetValue(key, out var entry)) return;

        // Stale entries roll off the window automatically.
        if (DateTime.UtcNow - entry.FirstFailureUtc > Window)
        {
            _entries.TryRemove(key, out _);
            return;
        }

        if (entry.Failures >= MaxFailuresInWindow)
            throw new AuthFailedException("Too many failed attempts. Try again later.");
    }

    public void RecordFailure(string username)
    {
        var key = Normalise(username);
        var now = DateTime.UtcNow;

        _entries.AddOrUpdate(key,
            _ => new Entry { Failures = 1, FirstFailureUtc = now },
            (_, existing) =>
            {
                if (now - existing.FirstFailureUtc > Window)
                {
                    existing.Failures = 1;
                    existing.FirstFailureUtc = now;
                }
                else
                {
                    existing.Failures++;
                }
                return existing;
            });

        // Cheap eviction pass: if we're at the cap, drop the entries whose
        // window has expired. Best-effort — it's fine if a concurrent caller
        // re-adds during the sweep.
        if (_entries.Count > MaxEntries)
            EvictStale();
    }

    public void Clear(string username) => _entries.TryRemove(Normalise(username), out _);

    private void EvictStale()
    {
        var cutoff = DateTime.UtcNow - Window;
        foreach (var kv in _entries)
            if (kv.Value.FirstFailureUtc < cutoff)
                _entries.TryRemove(kv.Key, out _);
    }

    // Username comparison is case-insensitive elsewhere in the code (the unique
    // index on Users.Username is exact, but we normalise here so "Sverre" and
    // "sverre" share a counter).
    private static string Normalise(string s) => (s ?? "").Trim().ToLowerInvariant();
}
