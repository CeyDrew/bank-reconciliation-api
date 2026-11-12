using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Reconciliation.Infrastructure.Locking;

namespace Reconciliation.UnitTests.Infrastructure;

public class InMemoryDistributedLockTests
{
    private readonly FakeTimeProvider _clock = new();
    private readonly InMemoryDistributedLock _lock;

    public InMemoryDistributedLockTests()
    {
        _lock = new InMemoryDistributedLock(_clock);
    }

    [Fact]
    public async Task Second_acquire_fails_until_first_is_released()
    {
        var first = await _lock.TryAcquireAsync("job", TimeSpan.FromMinutes(1), CancellationToken.None);
        var second = await _lock.TryAcquireAsync("job", TimeSpan.FromMinutes(1), CancellationToken.None);

        first.Should().NotBeNull();
        second.Should().BeNull();

        await first!.DisposeAsync();
        (await _lock.TryAcquireAsync("job", TimeSpan.FromMinutes(1), CancellationToken.None)).Should().NotBeNull();
    }

    [Fact]
    public async Task Expired_lock_can_be_taken_and_old_holder_cannot_release_it()
    {
        var stale = await _lock.TryAcquireAsync("job", TimeSpan.FromMinutes(1), CancellationToken.None);
        _clock.Advance(TimeSpan.FromMinutes(2));

        var fresh = await _lock.TryAcquireAsync("job", TimeSpan.FromMinutes(1), CancellationToken.None);
        await stale!.DisposeAsync();

        fresh.Should().NotBeNull();
        (await _lock.TryAcquireAsync("job", TimeSpan.FromMinutes(1), CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Different_resources_do_not_block_each_other()
    {
        (await _lock.TryAcquireAsync("a", TimeSpan.FromMinutes(1), CancellationToken.None)).Should().NotBeNull();
        (await _lock.TryAcquireAsync("b", TimeSpan.FromMinutes(1), CancellationToken.None)).Should().NotBeNull();
    }
}
