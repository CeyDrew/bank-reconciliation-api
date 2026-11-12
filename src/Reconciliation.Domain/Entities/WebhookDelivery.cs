using Reconciliation.Domain.Common;
using Reconciliation.Domain.Enums;

namespace Reconciliation.Domain.Entities;

/// <summary>
/// An event waiting to be sent to the ERP (transactional outbox). It is stored in the same database
/// transaction as the change it describes, so an event can't be lost or sent for a change that was rolled back.
/// The Id doubles as the idempotency key the ERP uses to ignore repeated deliveries.
/// </summary>
public class WebhookDelivery : Entity
{
    public const int MaxErrorLength = 1000;
    private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromHours(1);

    public string EventType { get; private init; } = null!;

    /// <summary>The exact JSON body that is signed and sent.</summary>
    public string Payload { get; private init; } = null!;

    public WebhookStatus Status { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset CreatedAt { get; private init; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }
    public string? LastError { get; private set; }

    private WebhookDelivery() { }

    public WebhookDelivery(Guid id, string eventType, string payload, DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(eventType) || string.IsNullOrWhiteSpace(payload))
        {
            throw new DomainException("Webhook event type and payload are required.");
        }

        Id = id;
        EventType = eventType;
        Payload = payload;
        Status = WebhookStatus.Pending;
        CreatedAt = createdAt;
        NextAttemptAt = createdAt;
    }

    public bool IsDue(DateTimeOffset now) => Status == WebhookStatus.Pending && NextAttemptAt <= now;

    public void MarkDelivered(DateTimeOffset now)
    {
        EnsurePending();
        Attempts++;
        Status = WebhookStatus.Delivered;
        DeliveredAt = now;
        LastError = null;
    }

    /// <summary>A temporary problem (timeout, 5xx): try again later, waiting longer each time, until attempts run out.</summary>
    public void MarkAttemptFailed(string error, DateTimeOffset now, int maxAttempts)
    {
        EnsurePending();
        Attempts++;
        LastError = Truncate(error);

        if (Attempts >= maxAttempts)
        {
            Status = WebhookStatus.Failed;
            return;
        }

        NextAttemptAt = now + RetryDelay(Attempts);
    }

    /// <summary>The ERP rejected the event (4xx): retrying won't help, so a person has to look at it.</summary>
    public void MarkRejected(string error)
    {
        EnsurePending();
        Attempts++;
        LastError = Truncate(error);
        Status = WebhookStatus.Failed;
    }

    /// <summary>Sends a failed event again, e.g. after the ERP has been fixed.</summary>
    public void Requeue(DateTimeOffset now)
    {
        if (Status != WebhookStatus.Failed)
        {
            throw new DomainException("Only failed webhook deliveries can be retried.");
        }

        Status = WebhookStatus.Pending;
        Attempts = 0;
        NextAttemptAt = now;
    }

    /// <summary>30 s, 1 min, 2 min, 4 min ... capped at one hour.</summary>
    public static TimeSpan RetryDelay(int attempts) =>
        TimeSpan.FromSeconds(Math.Min(FirstRetryDelay.TotalSeconds * Math.Pow(2, attempts - 1), MaxRetryDelay.TotalSeconds));

    private void EnsurePending()
    {
        if (Status != WebhookStatus.Pending)
        {
            throw new DomainException($"Webhook delivery is {Status}, not pending.");
        }
    }

    private static string Truncate(string error) => error.Length <= MaxErrorLength ? error : error[..MaxErrorLength];
}
