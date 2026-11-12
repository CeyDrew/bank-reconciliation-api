using System.Collections.Concurrent;
using Reconciliation.Application.Abstractions;

namespace Reconciliation.Infrastructure.Locking;

/// <summary>
/// Fallback when Redis isn't configured (tests, single-instance development).
/// Only protects against concurrent runs inside this process.
/// </summary>
public class InMemoryDistributedLock(TimeProvider clock) : IDistributedLock
{
    private readonly ConcurrentDictionary<string, (Guid Token, DateTimeOffset ExpiresAt)> _locks = new();

    public Task<IAsyncDisposable?> TryAcquireAsync(string resource, TimeSpan expiry, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var entry = (Token: Guid.NewGuid(), ExpiresAt: now + expiry);

        var acquired = _locks.TryAdd(resource, entry)
            || (_locks.TryGetValue(resource, out var current) && current.ExpiresAt <= now && _locks.TryUpdate(resource, entry, current));

        return Task.FromResult<IAsyncDisposable?>(acquired ? new Handle(_locks, resource, entry) : null);
    }

    private sealed class Handle(
        ConcurrentDictionary<string, (Guid Token, DateTimeOffset ExpiresAt)> locks,
        string resource,
        (Guid Token, DateTimeOffset ExpiresAt) entry) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            // Removes only our own entry, never one taken over after expiry.
            locks.TryRemove(new KeyValuePair<string, (Guid, DateTimeOffset)>(resource, entry));
            return ValueTask.CompletedTask;
        }
    }
}
