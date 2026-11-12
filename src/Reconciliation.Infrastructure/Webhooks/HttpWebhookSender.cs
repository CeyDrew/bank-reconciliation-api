using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Reconciliation.Application.Webhooks;
using Reconciliation.Domain.Entities;

namespace Reconciliation.Infrastructure.Webhooks;

/// <summary>Bound from "Webhooks". Delivery is switched off while Url is empty; events still queue up.</summary>
public class WebhookOptions
{
    public const string SectionName = "Webhooks";

    public string Url { get; init; } = "";

    /// <summary>Shared HMAC secret, at least 32 characters. Comes from .env (WEBHOOK_SECRET).</summary>
    public string Secret { get; init; } = "";

    [Range(1, 3600)]
    public int DispatchIntervalSeconds { get; init; } = 10;

    public bool Enabled => !string.IsNullOrWhiteSpace(Url);
}

public static class WebhookHeaders
{
    public const string Id = "X-Webhook-Id";
    public const string Event = "X-Webhook-Event";
    public const string Timestamp = "X-Webhook-Timestamp";
    public const string Signature = "X-Webhook-Signature";
    public const string IdempotencyKey = "Idempotency-Key";
}

public class HttpWebhookSender(HttpClient http, IOptions<WebhookOptions> options, TimeProvider clock) : IWebhookSender
{
    public async Task<WebhookSendResult> SendAsync(WebhookDelivery delivery, CancellationToken ct)
    {
        var settings = options.Value;
        var timestamp = clock.GetUtcNow().ToUnixTimeSeconds();

        using var request = new HttpRequestMessage(HttpMethod.Post, settings.Url)
        {
            Content = new StringContent(delivery.Payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add(WebhookHeaders.Id, delivery.Id.ToString());
        request.Headers.Add(WebhookHeaders.IdempotencyKey, delivery.Id.ToString());
        request.Headers.Add(WebhookHeaders.Event, delivery.EventType);
        request.Headers.Add(WebhookHeaders.Timestamp, timestamp.ToString(CultureInfo.InvariantCulture));
        request.Headers.Add(WebhookHeaders.Signature, WebhookSignature.Create(settings.Secret, timestamp, delivery.Payload));

        try
        {
            using var response = await http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                return new WebhookSendResult(WebhookSendOutcome.Delivered);
            }

            var error = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
            return IsPermanent(response.StatusCode)
                ? new WebhookSendResult(WebhookSendOutcome.Rejected, error)
                : new WebhookSendResult(WebhookSendOutcome.TransientFailure, error);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Connection errors, Polly timeouts and an open circuit breaker all mean "try later".
            return new WebhookSendResult(WebhookSendOutcome.TransientFailure, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static bool IsPermanent(HttpStatusCode status) =>
        (int)status is >= 400 and < 500 && status is not (HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests);
}
