using System.Collections.Concurrent;

namespace PortfolioApi.Services;

public sealed class CommentThrottle : ICommentThrottle
{
    // 5 comments per minute is generous enough that no real conversation
    // hits the cap, but tight enough that a script burst gets shut down.
    private const int MaxInWindow = 5;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private const int MaxEntries = 50_000;

    // List of recent timestamps per user. We don't bother with a fixed-size
    // ring — the window cap is small.
    private readonly ConcurrentDictionary<Guid, List<DateTime>> _hits = new();

    public void EnsureCanComment(Guid userId)
    {
        if (!_hits.TryGetValue(userId, out var stamps)) return;
        var cutoff = DateTime.UtcNow - Window;
        lock (stamps)
        {
            stamps.RemoveAll(t => t < cutoff);
            if (stamps.Count >= MaxInWindow)
                throw new AuthFailedException("Slow down — too many comments in a minute.");
        }
    }

    public void Record(Guid userId)
    {
        var list = _hits.GetOrAdd(userId, _ => new List<DateTime>());
        lock (list)
        {
            var now = DateTime.UtcNow;
            list.Add(now);
            // Trim while we're holding the lock.
            var cutoff = now - Window;
            list.RemoveAll(t => t < cutoff);
        }

        // Cheap eviction to keep the dictionary bounded under spray.
        if (_hits.Count > MaxEntries) Evict();
    }

    private void Evict()
    {
        var cutoff = DateTime.UtcNow - Window;
        foreach (var kv in _hits)
        {
            lock (kv.Value)
            {
                if (kv.Value.Count == 0 || kv.Value[^1] < cutoff)
                    _hits.TryRemove(kv.Key, out _);
            }
        }
    }
}
