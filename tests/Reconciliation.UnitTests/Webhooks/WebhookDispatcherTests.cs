using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Reconciliation.Application.Webhooks;
using Reconciliation.Domain.Entities;
using Reconciliation.Domain.Enums;
using Reconciliation.Infrastructure.Locking;
using Reconciliation.Infrastructure.Persistence;

namespace Reconciliation.UnitTests.Webhooks;

public class WebhookDispatcherTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero));
    private readonly AppDbContext _db = TestDb.Create();
    private readonly InMemoryDistributedLock _lock;
    private readonly ScriptedSender _sender = new();

    public WebhookDispatcherTests()
    {
        _lock = new InMemoryDistributedLock(_clock);
    }

    [Fact]
    public async Task Pending_events_are_sent_oldest_first_and_marked_delivered()
    {
        var first = Enqueue("match.created");
        var second = Enqueue("match.reversed");

        var result = await Dispatch();

        result.Should().Be(new WebhookDispatchResult(2, 0, 0));
        _sender.Sent.Should().Equal(first.Id, second.Id);
        _db.WebhookDeliveries.Should().OnlyContain(d => d.Status == WebhookStatus.Delivered && d.Attempts == 1 && d.DeliveredAt == _clock.GetUtcNow());
    }

    [Fact]
    public async Task Transient_failure_schedules_a_retry_and_stops_the_batch_to_keep_order()
    {
        var first = Enqueue("match.created");
        var second = Enqueue("match.reversed");
        _sender.Next(WebhookSendOutcome.TransientFailure, "HTTP 503");

        var result = await Dispatch();

        result.Should().Be(new WebhookDispatchResult(0, 1, 0));
        _sender.Sent.Should().Equal(first.Id); // the second event did not overtake the first
        first.Attempts.Should().Be(1);
        first.LastError.Should().Be("HTTP 503");
        first.NextAttemptAt.Should().Be(_clock.GetUtcNow().AddSeconds(30));
        second.Status.Should().Be(WebhookStatus.Pending);
    }

    [Fact]
    public async Task Event_waiting_for_retry_is_not_sent_early_and_blocks_later_events()
    {
        Enqueue("match.created");
        Enqueue("match.reversed");
        _sender.Next(WebhookSendOutcome.TransientFailure, "timeout");
        await Dispatch();

        _clock.Advance(TimeSpan.FromSeconds(10));
        await Dispatch();
        _sender.Sent.Should().HaveCount(1);

        _clock.Advance(TimeSpan.FromSeconds(30));
        var result = await Dispatch();
        result.Should().Be(new WebhookDispatchResult(2, 0, 0));
    }

    [Fact]
    public async Task Rejected_event_fails_at_once_and_the_rest_continue()
    {
        var bad = Enqueue("match.created");
        var good = Enqueue("match.created");
        _sender.Next(WebhookSendOutcome.Rejected, "HTTP 400");

        var result = await Dispatch();

        result.Should().Be(new WebhookDispatchResult(1, 0, 1));
        bad.Status.Should().Be(WebhookStatus.Failed);
        good.Status.Should().Be(WebhookStatus.Delivered);
    }

    [Fact]
    public async Task Event_fails_after_max_attempts_and_can_be_requeued()
    {
        var delivery = Enqueue("match.created");
        for (var i = 0; i < 3; i++)
        {
            _sender.Next(WebhookSendOutcome.TransientFailure, "down");
            await Dispatch(maxAttempts: 3);
            _clock.Advance(TimeSpan.FromHours(1));
        }

        delivery.Status.Should().Be(WebhookStatus.Failed);
        delivery.Attempts.Should().Be(3);

        delivery.Requeue(_clock.GetUtcNow());
        await _db.SaveChangesAsync();
        await Dispatch(maxAttempts: 3);
        delivery.Status.Should().Be(WebhookStatus.Delivered);
    }

    [Fact]
    public async Task Skips_while_another_dispatcher_holds_the_lock()
    {
        Enqueue("match.created");
        await using var held = await _lock.TryAcquireAsync(WebhookDispatcher.LockName, TimeSpan.FromMinutes(1), CancellationToken.None);

        (await Dispatch()).Should().BeNull();
        _sender.Sent.Should().BeEmpty();
    }

    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 60)]
    [InlineData(5, 480)]
    [InlineData(20, 3600)] // capped at one hour
    public void Retry_delay_doubles_up_to_one_hour(int attempts, int expectedSeconds)
    {
        WebhookDelivery.RetryDelay(attempts).Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    private Task<WebhookDispatchResult?> Dispatch(int maxAttempts = 10) =>
        new WebhookDispatcher(_db, _sender, _lock, _clock,
                Options.Create(new WebhookDispatchOptions { MaxAttempts = maxAttempts }), NullLogger<WebhookDispatcher>.Instance)
            .DispatchAsync(CancellationToken.None);

    private WebhookDelivery Enqueue(string eventType)
    {
        var delivery = new WebhookDelivery(Guid.NewGuid(), eventType, "{}", _clock.GetUtcNow());
        _db.WebhookDeliveries.Add(delivery);
        _db.SaveChanges();
        _clock.Advance(TimeSpan.FromMilliseconds(1)); // distinct creation times
        return delivery;
    }

    /// <summary>Delivers everything unless told otherwise for the next call.</summary>
    private sealed class ScriptedSender : IWebhookSender
    {
        private readonly Queue<WebhookSendResult> _script = new();

        public List<Guid> Sent { get; } = [];

        public void Next(WebhookSendOutcome outcome, string error) => _script.Enqueue(new WebhookSendResult(outcome, error));

        public Task<WebhookSendResult> SendAsync(WebhookDelivery delivery, CancellationToken ct)
        {
            Sent.Add(delivery.Id);
            return Task.FromResult(_script.TryDequeue(out var result) ? result : new WebhookSendResult(WebhookSendOutcome.Delivered));
        }
    }
}
