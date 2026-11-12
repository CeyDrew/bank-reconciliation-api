using Reconciliation.Domain.Common;

namespace Reconciliation.Domain.Entities;

/// <summary>
/// Append-only record of a business action: who did what to which record, and when.
/// Written in the same database transaction as the change itself.
/// </summary>
public class AuditEntry : Entity
{
    public DateTimeOffset OccurredAt { get; private init; }

    /// <summary>Null for actions of the system itself (scheduled jobs).</summary>
    public Guid? UserId { get; private init; }

    public string UserName { get; private init; } = null!;

    /// <summary>Dotted action name, e.g. "match.created" or "invoice.cancelled".</summary>
    public string Action { get; private init; } = null!;

    public string EntityType { get; private init; } = null!;
    public Guid? EntityId { get; private init; }

    /// <summary>JSON with the relevant values. Never contains secrets; IBANs are masked.</summary>
    public string? Details { get; private init; }

    private AuditEntry() { }

    public AuditEntry(DateTimeOffset occurredAt, Guid? userId, string userName, string action, string entityType,
        Guid? entityId, string? details)
    {
        if (string.IsNullOrWhiteSpace(action) || string.IsNullOrWhiteSpace(entityType))
        {
            throw new DomainException("Audit action and entity type are required.");
        }

        OccurredAt = occurredAt;
        UserId = userId;
        UserName = userName;
        Action = action;
        EntityType = entityType;
        EntityId = entityId;
        Details = details;
    }
}
