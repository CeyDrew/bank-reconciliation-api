using FluentAssertions;
using Reconciliation.Infrastructure.Locking;
using Reconciliation.IntegrationTests.Infrastructure;
using StackExchange.Redis;

namespace Reconciliation.IntegrationTests;

[Collection(ContainersCollection.Name)]
public class RedisLockTests(ContainersFixture containers) : IAsyncLifetime
{
    private ConnectionMultiplexer _redis = null!;
    private RedisDistributedLock _lock = null!;
    private readonly string _resource = $"locks:test:{Guid.NewGuid():N}";

    public async Task InitializeAsync()
    {
        _redis = await ConnectionMultiplexer.ConnectAsync(containers.RedisConnectionString);
        _lock = new RedisDistributedLock(_redis);
    }

    public async Task DisposeAsync() => await _redis.DisposeAsync();

    [Fact]
    public async Task Only_one_holder_at_a_time()
    {
        var first = await _lock.TryAcquireAsync(_resource, TimeSpan.FromMinutes(1), CancellationToken.None);
        var second = await _lock.TryAcquireAsync(_resource, TimeSpan.FromMinutes(1), CancellationToken.None);

        first.Should().NotBeNull();
        second.Should().BeNull();

        await first!.DisposeAsync();
        (await _lock.TryAcquireAsync(_resource, TimeSpan.FromMinutes(1), CancellationToken.None)).Should().NotBeNull();
    }

    [Fact]
    public async Task Lock_expires_and_the_old_holder_cannot_release_the_new_one()
    {
        var stale = await _lock.TryAcquireAsync(_resource, TimeSpan.FromMilliseconds(300), CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(600));

        var fresh = await _lock.TryAcquireAsync(_resource, TimeSpan.FromMinutes(1), CancellationToken.None);
        await stale!.DisposeAsync(); // compare-and-delete: must not remove the fresh holder's key

        fresh.Should().NotBeNull();
        (await _lock.TryAcquireAsync(_resource, TimeSpan.FromMinutes(1), CancellationToken.None)).Should().BeNull();
    }
}
