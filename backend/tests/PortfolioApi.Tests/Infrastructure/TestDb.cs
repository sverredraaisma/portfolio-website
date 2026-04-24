using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PortfolioApi.Data;

namespace PortfolioApi.Tests.Infrastructure;

/// SQLite-in-memory variant of AppDbContext for service-level integration
/// tests. AppDbContext.OnModelCreating gates Postgres-specific column types
/// and GIN indexes behind a Database.IsNpgsql() check so the model also
/// builds on SQLite. Each instance gets its own private connection so tests
/// don't bleed across each other.
public sealed class TestDb : IDisposable
{
    public AppDbContext Db { get; }
    private readonly SqliteConnection _conn;

    public TestDb()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        Db = new AppDbContext(opts);
        Db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Db.Dispose();
        _conn.Dispose();
    }
}
