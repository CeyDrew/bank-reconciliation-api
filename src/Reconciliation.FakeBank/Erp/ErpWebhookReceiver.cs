using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Reconciliation.FakeBank.Erp;

public class ErpOptions
{
    public const string SectionName = "Erp";

    /// <summary>Shared HMAC secret; must equal the API's Webhooks:Secret.</summary>
    public string WebhookSecret { get; init; } = "";

    /// <summary>Requests older or newer than this are rejected as possible replays.</summary>
    public int TimestampToleranceSeconds { get; init; } = 300;

    /// <summary>
    /// Share of accepted webhooks answered with 503 anyway, as if the response got lost.
    /// The sender then retries, and the idempotency key keeps the ERP from booking the payment twice.
    /// </summary>
    public double FailAfterAcceptRate { get; init; }
}

public enum VerificationResult
{
    Valid,
    MissingHeaders,
    StaleTimestamp,
    BadSignature
}

/// <summary>What a real receiver must check before trusting a webhook. Written independently of the API's signer on purpose.</summary>
public class WebhookVerifier(IOptions<ErpOptions> options, TimeProvider clock)
{
    public VerificationResult Verify(string? timestampHeader, string? signatureHeader, string body)
    {
        if (!long.TryParse(timestampHeader, NumberStyles.None, CultureInfo.InvariantCulture, out var timestamp)
            || string.IsNullOrEmpty(signatureHeader))
        {
            return VerificationResult.MissingHeaders;
        }

        var age = Math.Abs(clock.GetUtcNow().ToUnixTimeSeconds() - timestamp);
        if (age > options.Value.TimestampToleranceSeconds)
        {
            return VerificationResult.StaleTimestamp;
        }

        var key = Encoding.UTF8.GetBytes(options.Value.WebhookSecret);
        var expected = "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"{timestamp}.{body}")));

        // Constant-time comparison: the time taken must not reveal how many characters matched.
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(signatureHeader))
            ? VerificationResult.Valid
            : VerificationResult.BadSignature;
    }
}

public record ReceivedWebhook(string Id, string EventType, DateTimeOffset ReceivedAt, string Body);

/// <summary>The fake ERP's memory of processed events, keyed by idempotency key.</summary>
public class ErpInbox
{
    private readonly ConcurrentDictionary<string, ReceivedWebhook> _received = new();
    private int _duplicates;

    /// <summary>False when this id was processed before; the event must not be booked again.</summary>
    public bool TryAccept(ReceivedWebhook webhook) => _received.TryAdd(webhook.Id, webhook);

    public void CountDuplicate() => Interlocked.Increment(ref _duplicates);

    public object Summary() => new
    {
        Received = _received.Count,
        DuplicatesIgnored = _duplicates,
        ByType = _received.Values.GroupBy(w => w.EventType).ToDictionary(g => g.Key, g => g.Count()),
        Latest = _received.Values.OrderByDescending(w => w.ReceivedAt).Take(10)
    };
}
