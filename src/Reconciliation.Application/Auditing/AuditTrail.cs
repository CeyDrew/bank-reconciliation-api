using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Reconciliation.Application.Abstractions;
using Reconciliation.Application.Common;
using Reconciliation.Domain.Entities;

namespace Reconciliation.Application.Auditing;

/// <summary>
/// Adds audit entries to the unit of work. They are saved by the caller's SaveChanges,
/// so an action and its audit entry are stored together or not at all.
/// </summary>
public class AuditTrail(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider clock)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Record(string action, string entityType, Guid? entityId, object? details = null) =>
        RecordAs(currentUser.UserId, currentUser.UserName, action, entityType, entityId, details);

    /// <summary>For actions where the actor isn't the signed-in user yet, such as a login.</summary>
    public void RecordAs(Guid? userId, string userName, string action, string entityType, Guid? entityId, object? details = null) =>
        db.AuditEntries.Add(new AuditEntry(clock.GetUtcNow(), userId, userName, action, entityType, entityId,
            details is null ? null : JsonSerializer.Serialize(details, JsonOptions)));
}

public record AuditEntryResponse(
    Guid Id, DateTimeOffset OccurredAt, Guid? UserId, string UserName, string Action, string EntityType, Guid? EntityId, string? Details)
{
    public static AuditEntryResponse From(AuditEntry e) =>
        new(e.Id, e.OccurredAt, e.UserId, e.UserName, e.Action, e.EntityType, e.EntityId, e.Details);
}

public record AuditQuery : PageRequest
{
    public string? EntityType { get; init; }
    public Guid? EntityId { get; init; }
    public string? Action { get; init; }
    public string? UserName { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
}

public class AuditQueries(IApplicationDbContext db)
{
    public Task<PagedResult<AuditEntryResponse>> ListAsync(AuditQuery query, CancellationToken ct)
    {
        var entries = db.AuditEntries.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.EntityType))
        {
            entries = entries.Where(e => e.EntityType == query.EntityType);
        }

        if (query.EntityId is { } entityId)
        {
            entries = entries.Where(e => e.EntityId == entityId);
        }

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            entries = entries.Where(e => e.Action == query.Action);
        }

        if (!string.IsNullOrWhiteSpace(query.UserName))
        {
            entries = entries.Where(e => e.UserName == query.UserName);
        }

        if (query.From is { } from)
        {
            entries = entries.Where(e => e.OccurredAt >= from);
        }

        if (query.To is { } to)
        {
            entries = entries.Where(e => e.OccurredAt <= to);
        }

        return entries.OrderByDescending(e => e.OccurredAt).ToPagedResultAsync(query, AuditEntryResponse.From, ct);
    }
}
