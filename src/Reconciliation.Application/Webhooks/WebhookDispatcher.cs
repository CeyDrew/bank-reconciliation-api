using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Reconciliation.Application.Abstractions;
using Reconciliation.Domain.Entities;
using Reconciliation.Domain.Enums;

namespace Reconciliation.Application.Webhooks;

public class WebhookDispatchOptions
{
    public const string SectionName = "Webhooks";

    /// <summary>After this many failed attempts an event is marked Failed and waits for a person.</summary>
    public int MaxAttempts { get; init; } = 10;

    public int BatchSize { get; init; } = 50;
}

public enum WebhookSendOutcome
{
    Delivered,

    /// <summary>Timeout, connection error, 5xx, 408 or 429: worth trying again.</summary>
    TransientFailure,

    /// <summary>Any other 4xx: the ERP won't accept this event however often it is sent.</summary>
    Rejected
}

public record WebhookSendResult(WebhookSendOutcome Outcome, string? Error = null);

public interface IWebhookSender
{
    Task<WebhookSendResult> SendAsync(WebhookDelivery delivery, CancellationToken ct);
}

public record WebhookDispatchResult(int Delivered, int Retrying, int Failed);

/// <summary>
/// Sends pending outbox events in the order they were created. Delivery is at least once:
/// a crash between sending and saving re-sends one event, which the ERP ignores thanks to the idempotency key.
/// </summary>
public class WebhookDispatcher(
    IApplicationDbContext db,
    IWebhookSender sender,
    IDistributedLock distributedLock,
    TimeProvider clock,
    IOptions<WebhookDispatchOptions> options,
    ILogger<WebhookDispatcher> logger)
{
    public const string LockName = "locks:webhook-dispatch";

    public async Task<WebhookDispatchResult?> DispatchAsync(CancellationToken ct)
    {
        await using var handle = await distributedLock.TryAcquireAsync(LockName, TimeSpan.FromMinutes(5), ct);
        if (handle is null)
        {
            return null;
        }

        var settings = options.Value;
        var batch = await db.WebhookDeliveries
            .Where(d => d.Status == WebhookStatus.Pending)
            .OrderBy(d => d.CreatedAt).ThenBy(d => d.Id)
            .Take(settings.BatchSize)
            .ToListAsync(ct);

        int delivered = 0, retrying = 0, failed = 0;
        foreach (var delivery in batch)
        {
            // Never overtake an older event that is waiting for its retry: the ERP must see
            // "match.created" before the "match.reversed" of the same match.
            if (!delivery.IsDue(clock.GetUtcNow()))
            {
                break;
            }

            var result = await sender.SendAsync(delivery, ct);
            var now = clock.GetUtcNow();
            switch (result.Outcome)
            {
                case WebhookSendOutcome.Delivered:
                    delivery.MarkDelivered(now);
                    delivered++;
                    break;
                case WebhookSendOutcome.Rejected:
                    delivery.MarkRejected(result.Error ?? "Rejected");
                    failed++;
                    logger.LogError("Webhook {DeliveryId} ({EventType}) rejected by the ERP: {Error}",
                        delivery.Id, delivery.EventType, result.Error);
                    break;
                default:
                    delivery.MarkAttemptFailed(result.Error ?? "Unknown error", now, settings.MaxAttempts);
                    if (delivery.Status == WebhookStatus.Failed)
                    {
                        failed++;
                        logger.LogError("Webhook {DeliveryId} ({EventType}) failed after {Attempts} attempts: {Error}",
                            delivery.Id, delivery.EventType, delivery.Attempts, result.Error);
                    }
                    else
                    {
                        retrying++;
                        logger.LogWarning("Webhook {DeliveryId} attempt {Attempts} failed, next try at {NextAttemptAt}: {Error}",
                            delivery.Id, delivery.Attempts, delivery.NextAttemptAt, result.Error);
                    }

                    break;
            }

            // Save after every event so a crash re-sends at most one of them.
            await db.SaveChangesAsync(ct);

            if (delivery.Status == WebhookStatus.Pending)
            {
                break; // the ERP is struggling; keep the order and continue on a later run
            }
        }

        var summary = new WebhookDispatchResult(delivered, retrying, failed);
        if (batch.Count > 0)
        {
            logger.LogInformation("Webhook dispatch finished: {@Result}", summary);
        }

        return summary;
    }
}
