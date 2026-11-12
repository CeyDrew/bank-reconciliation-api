namespace Reconciliation.Application.Abstractions;

/// <summary>A lock shared by every running instance of the API (Redis in production).</summary>
public interface IDistributedLock
{
    /// <summary>
    /// Tries to take the lock without waiting. Returns a handle that releases it on dispose,
    /// or null when someone else holds it. The lock expires by itself after <paramref name="expiry"/>
    /// so a crashed holder can't block others forever.
    /// </summary>
    Task<IAsyncDisposable?> TryAcquireAsync(string resource, TimeSpan expiry, CancellationToken ct);
}
