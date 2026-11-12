using Microsoft.EntityFrameworkCore;
using Reconciliation.Application.Abstractions;
using Reconciliation.Application.Auditing;
using Reconciliation.Application.Common;
using Reconciliation.Domain.Entities;
using Reconciliation.Domain.Enums;

namespace Reconciliation.Application.Webhooks;

public record WebhookDeliveryResponse(
    Guid Id,
    string EventType,
    WebhookStatus Status,
    int Attempts,
    DateTimeOffset CreatedAt,
    DateTimeOffset NextAttemptAt,
    DateTimeOffset? DeliveredAt,
    string? LastError,
    string Payload)
{
    public static WebhookDeliveryResponse From(WebhookDelivery d) =>
        new(d.Id, d.EventType, d.Status, d.Attempts, d.CreatedAt, d.NextAttemptAt, d.DeliveredAt, d.LastError, d.Payload);
}

public record WebhookDeliveryQuery : PageRequest
{
    public WebhookStatus? Status { get; init; }
}

public class WebhookAdminService(IApplicationDbContext db, AuditTrail audit, TimeProvider clock)
{
    public Task<PagedResult<WebhookDeliveryResponse>> ListAsync(WebhookDeliveryQuery query, CancellationToken ct)
    {
        var deliveries = db.WebhookDeliveries.AsNoTracking();
        if (query.Status is { } status)
        {
            deliveries = deliveries.Where(d => d.Status == status);
        }

        return deliveries.OrderByDescending(d => d.CreatedAt).ToPagedResultAsync(query, WebhookDeliveryResponse.From, ct);
    }

    public async Task<WebhookDeliveryResponse> RetryAsync(Guid id, CancellationToken ct)
    {
        var delivery = await db.WebhookDeliveries.FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new NotFoundException("Webhook delivery", id);

        delivery.Requeue(clock.GetUtcNow());
        audit.Record("webhook.requeued", "WebhookDelivery", delivery.Id, new { delivery.EventType, delivery.LastError });
        await db.SaveChangesAsync(ct);

        return WebhookDeliveryResponse.From(delivery);
    }
}
