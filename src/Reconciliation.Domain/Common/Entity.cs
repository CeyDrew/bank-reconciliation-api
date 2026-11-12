namespace Reconciliation.Domain.Common;

public abstract class Entity
{
    /// <summary>Assigned in code (time-ordered version 7 GUID) so an entity has its identity before it is saved.</summary>
    public Guid Id { get; protected init; } = Guid.CreateVersion7();
}
