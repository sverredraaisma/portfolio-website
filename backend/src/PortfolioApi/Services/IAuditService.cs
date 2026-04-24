using PortfolioApi.Models;

namespace PortfolioApi.Services;

/// Records sensitive actions on a user's account. Writes go directly to the
/// AppDbContext; the caller is responsible for owning the SaveChanges (so an
/// audit row only commits if the action it describes also commits).
public interface IAuditService
{
    /// Stages an audit row on the supplied DbContext but does not save. The
    /// caller's outer SaveChanges flushes both the action and the audit row
    /// in the same transaction.
    void Record(Guid userId, string kind, string? detail = null);
}
