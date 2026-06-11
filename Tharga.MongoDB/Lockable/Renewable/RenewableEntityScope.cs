using System;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;

namespace Tharga.MongoDB.Lockable.Renewable;

/// <summary>
/// Renewable variant of <see cref="EntityScope{T}"/>. Behaves identically for commit / abandon /
/// error-state, but additionally exposes lease-renewal: <c>ExtendAsync</c> for an explicit
/// keep-alive, <c>StartKeepAlive</c> for an automatic background loop, and <c>LockLost</c>
/// which is cancelled if a renewal discovers the lock has been lost.
/// </summary>
public record RenewableEntityScope<T> : RenewableEntityScope<T, ObjectId>
    where T : LockableEntityBase<ObjectId>
{
    internal RenewableEntityScope(T entity, Func<T, bool, Exception, Task> releaseAction, RenewalController controller)
        : base(entity, releaseAction, controller)
    {
    }
}

/// <summary>
/// Renewable variant of <see cref="EntityScope{T, TKey}"/>. Behaves identically for commit / abandon /
/// error-state, but additionally exposes lease-renewal: <c>ExtendAsync</c> for an explicit
/// keep-alive, <c>StartKeepAlive</c> for an automatic background loop, and <c>LockLost</c>
/// which is cancelled if a renewal discovers the lock has been lost.
/// </summary>
public record RenewableEntityScope<T, TKey> : EntityScope<T, TKey>
    where T : LockableEntityBase<TKey>
{
    private readonly RenewalController _controller;

    internal RenewableEntityScope(T entity, Func<T, bool, Exception, Task> releaseAction, RenewalController controller)
        : base(entity, releaseAction)
    {
        _controller = controller;
    }

    /// <summary>
    /// Cancelled when a renewal attempt discovers the lock has been lost (document deleted or
    /// re-locked by another writer). Long-running work can observe this token to abort early.
    /// </summary>
    public CancellationToken LockLost => _controller.LockLost;

    /// <summary>
    /// Extends the lease, pushing <c>ExpireTime</c> further into the future. Returns the new <c>ExpireTime</c>.
    /// </summary>
    /// <param name="extension">How far to extend. When <c>null</c>, reuses the lease's current span.</param>
    public Task<DateTime> ExtendAsync(TimeSpan? extension = null) => _controller.ExtendAsync(extension);

    /// <summary>
    /// Starts a background loop that periodically extends the lease until the returned handle is
    /// disposed or the lease is released. Dispose the handle (or release the scope) to stop it.
    /// </summary>
    public IAsyncDisposable StartKeepAlive(LockKeepAliveOptions options = null) => _controller.StartKeepAlive(options);
}
