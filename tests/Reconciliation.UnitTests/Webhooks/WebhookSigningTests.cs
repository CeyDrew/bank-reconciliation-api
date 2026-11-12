using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Reconciliation.Application.Webhooks;
using Reconciliation.Domain.Entities;
using Reconciliation.FakeBank.Erp;
using Reconciliation.Infrastructure.Webhooks;

namespace Reconciliation.UnitTests.Webhooks;

/// <summary>The API's signer and the fake ERP's independent verifier must agree.</summary>
public class WebhookSigningTests
{
    private static readonly string Secret = new('s', 40);
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Hmac_matches_the_rfc_4231_test_vector()
    {
        WebhookSignature.Hmac("Jefe", "what do ya want for nothing?")
            .Should().Be("5bdcc146bf60754e6a042426089575c75a003f089d2739839dec58b964ec3843");
    }

    [Fact]
    public async Task Sent_request_carries_headers_that_the_receiver_accepts()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK);
        var delivery = new WebhookDelivery(Guid.NewGuid(), "match.created", """{"id":"1","amount":12.50}""", _clock.GetUtcNow());

        var result = await CreateSender(handler).SendAsync(delivery, CancellationToken.None);

        result.Outcome.Should().Be(WebhookSendOutcome.Delivered);
        var request = handler.Request!;
        request.Headers.GetValues("Idempotency-Key").Should().Equal(delivery.Id.ToString());
        request.Headers.GetValues("X-Webhook-Event").Should().Equal("match.created");
        handler.Body.Should().Be(delivery.Payload);

        Verifier().Verify(request.Headers.GetValues("X-Webhook-Timestamp").Single(),
            request.Headers.GetValues("X-Webhook-Signature").Single(), handler.Body!).Should().Be(VerificationResult.Valid);
    }

    [Fact]
    public void Tampered_body_wrong_secret_and_old_timestamp_are_rejected()
    {
        var timestamp = _clock.GetUtcNow().ToUnixTimeSeconds();
        var signature = WebhookSignature.Create(Secret, timestamp, """{"amount":12.50}""");
        var verifier = Verifier();

        verifier.Verify(timestamp.ToString(), signature, """{"amount":99.50}""").Should().Be(VerificationResult.BadSignature);
        verifier.Verify(timestamp.ToString(), WebhookSignature.Create(new string('x', 40), timestamp, "{}"), "{}")
            .Should().Be(VerificationResult.BadSignature);
        verifier.Verify((timestamp - 301).ToString(), WebhookSignature.Create(Secret, timestamp - 301, "{}"), "{}")
            .Should().Be(VerificationResult.StaleTimestamp);
        verifier.Verify(null, signature, "{}").Should().Be(VerificationResult.MissingHeaders);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, WebhookSendOutcome.Delivered)]
    [InlineData(HttpStatusCode.Accepted, WebhookSendOutcome.Delivered)]
    [InlineData(HttpStatusCode.BadRequest, WebhookSendOutcome.Rejected)]
    [InlineData(HttpStatusCode.Unauthorized, WebhookSendOutcome.Rejected)]
    [InlineData(HttpStatusCode.RequestTimeout, WebhookSendOutcome.TransientFailure)]
    [InlineData(HttpStatusCode.TooManyRequests, WebhookSendOutcome.TransientFailure)]
    [InlineData(HttpStatusCode.ServiceUnavailable, WebhookSendOutcome.TransientFailure)]
    public async Task Status_code_decides_between_retry_and_dead_letter(HttpStatusCode status, WebhookSendOutcome expected)
    {
        var delivery = new WebhookDelivery(Guid.NewGuid(), "match.created", "{}", _clock.GetUtcNow());

        var result = await CreateSender(new CapturingHandler(status)).SendAsync(delivery, CancellationToken.None);

        result.Outcome.Should().Be(expected);
    }

    [Fact]
    public async Task Connection_error_is_transient()
    {
        var delivery = new WebhookDelivery(Guid.NewGuid(), "match.created", "{}", _clock.GetUtcNow());

        var result = await CreateSender(new CapturingHandler(error: new HttpRequestException("refused"))).SendAsync(delivery, CancellationToken.None);

        result.Should().Be(new WebhookSendResult(WebhookSendOutcome.TransientFailure, "HttpRequestException: refused"));
    }

    private HttpWebhookSender CreateSender(HttpMessageHandler handler) =>
        new(new HttpClient(handler), Options.Create(new WebhookOptions { Url = "http://erp.test/webhooks", Secret = Secret }), _clock);

    private WebhookVerifier Verifier() => new(Options.Create(new ErpOptions { WebhookSecret = Secret }), _clock);

    private sealed class CapturingHandler(HttpStatusCode status = HttpStatusCode.OK, Exception? error = null) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return error is null ? new HttpResponseMessage(status) : throw error;
        }
    }
}
