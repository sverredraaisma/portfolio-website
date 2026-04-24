using Microsoft.EntityFrameworkCore;
using PortfolioApi.Data;

namespace PortfolioApi.Services;

/// Periodically prunes refresh-token rows that no longer carry any value:
/// past their expiry, or revoked more than RetentionAfterRevoke ago. Without
/// this the table grows monotonically — every login + every rotation adds a
/// row, and revoked rows aren't useful for anything past the audit window.
///
/// Runs on the host's scheduler; sleeps Interval between sweeps. The first
/// sweep waits InitialDelay so deployment-time bursts (the seed admin's
/// boot, etc.) don't fight startup migrations for the connection.
public sealed class RefreshTokenCleanupService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<RefreshTokenCleanupService> _log;

    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan RetentionAfterRevoke = TimeSpan.FromDays(7);

    public RefreshTokenCleanupService(IServiceProvider services, ILogger<RefreshTokenCleanupService> log)
    {
        _services = services;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(InitialDelay, stoppingToken); }
        catch (TaskCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var deleted = await SweepOnceAsync(stoppingToken);
                if (deleted > 0)
                    _log.LogInformation("Refresh-token cleanup deleted {Count} stale rows.", deleted);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Swallow + log: a transient DB hiccup shouldn't crash the
                // host. We'll try again on the next interval.
                _log.LogError(ex, "Refresh-token cleanup sweep failed; will retry.");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (TaskCanceledException) { return; }
        }
    }

    /// Single cleanup pass. Returns the number of rows deleted. Public so
    /// tests can drive a sweep directly without standing up the host loop.
    public async Task<int> SweepOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var revokeCutoff = now - RetentionAfterRevoke;

        return await db.RefreshTokens
            .Where(t => t.ExpiresAt < now
                     || (t.RevokedAt != null && t.RevokedAt < revokeCutoff))
            .ExecuteDeleteAsync(cancellationToken);
    }
}
