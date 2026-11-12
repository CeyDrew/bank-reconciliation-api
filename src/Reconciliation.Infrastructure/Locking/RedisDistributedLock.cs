using Reconciliation.Application.Abstractions;
using StackExchange.Redis;

namespace Reconciliation.Infrastructure.Locking;

/// <summary>
/// Single-node Redis lock: SET key token NX PX expiry to take it, and a compare-and-delete
/// script to release it, so an instance can never delete a lock that has expired and been taken by another.
/// </summary>
public class RedisDistributedLock(IConnectionMultiplexer redis) : IDistributedLock
{
    private const string ReleaseScript =
        "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end";

    public async Task<IAsyncDisposable?> TryAcquireAsync(string resource, TimeSpan expiry, CancellationToken ct)
    {
        var database = redis.GetDatabase();
        var token = Guid.NewGuid().ToString("N");

        var acquired = await database.StringSetAsync(resource, token, expiry, When.NotExists);
        return acquired ? new Handle(database, resource, token) : null;
    }

    private sealed class Handle(IDatabase database, string resource, string token) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() =>
            await database.ScriptEvaluateAsync(ReleaseScript, [resource], [token]);
    }
}
