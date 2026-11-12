using System.Text.Json;
using System.Text.Json.Serialization;
using Reconciliation.Application.Abstractions;
using Reconciliation.Domain.Entities;

namespace Reconciliation.Application.Webhooks;

public static class WebhookEvents
{
    public const string MatchCreated = "match.created";
    public const string MatchReversed = "match.reversed";
}

/// <summary>
/// Adds events to the outbox. Like the audit trail it doesn't save: the caller's SaveChanges stores
/// the business change and its events together.
/// </summary>
public class WebhookOutbox(IApplicationDbContext db, TimeProvider clock)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public void MatchCreated(Match match) => Enqueue(WebhookEvents.MatchCreated, MatchData(match));

    /// <summary>Call after <see cref="Match.Reverse"/>, so the invoice figures are the reversed ones.</summary>
    public void MatchReversed(Match match, string reason) => Enqueue(WebhookEvents.MatchReversed, MatchData(match) with { Reason = reason });

    private void Enqueue(string eventType, object data)
    {
        var id = Guid.CreateVersion7();
        var now = clock.GetUtcNow();
        var payload = JsonSerializer.Serialize(new { id, type = eventType, occurredAt = now, data }, JsonOptions);
        db.WebhookDeliveries.Add(new WebhookDelivery(id, eventType, payload, now));
    }

    /// <summary>What the ERP needs to post the payment against its own invoice. Requires Transaction and Invoice loaded.</summary>
    private static MatchEventData MatchData(Match match) => new(
        match.Id,
        match.TransactionId,
        match.Transaction.BookingDate,
        match.InvoiceId,
        match.Invoice.Number,
        match.Amount,
        match.Invoice.Currency,
        match.Method.ToString(),
        match.Score,
        match.Invoice.Status.ToString(),
        match.Invoice.PaidAmount,
        match.Invoice.OpenAmount,
        Reason: null);

    private record MatchEventData(
        Guid MatchId,
        Guid TransactionId,
        DateOnly BookingDate,
        Guid InvoiceId,
        string InvoiceNumber,
        decimal Amount,
        string Currency,
        string Method,
        int Score,
        string InvoiceStatus,
        decimal InvoicePaidAmount,
        decimal InvoiceOpenAmount,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reason);
}
