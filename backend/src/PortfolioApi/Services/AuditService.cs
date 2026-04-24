using PortfolioApi.Data;
using PortfolioApi.Models;

namespace PortfolioApi.Services;

public sealed class AuditService : IAuditService
{
    private readonly AppDbContext _db;

    public AuditService(AppDbContext db) => _db = db;

    public void Record(Guid userId, string kind, string? detail = null)
    {
        _db.AuditEvents.Add(new AuditEvent { UserId = userId, Kind = kind, Detail = detail });
    }
}
