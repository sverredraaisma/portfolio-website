namespace PortfolioApi.Models;

/// Append-only record of sensitive account actions. Lets the user see at a
/// glance whether anything unexpected happened on their account, and gives
/// the controller something to reach for when investigating an incident.
///
/// `Kind` is a short stable identifier (see AuditKind) so future filtering
/// stays reliable; `Detail` is a free-form, optional human-readable note.
public class AuditEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public string Kind { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
}
