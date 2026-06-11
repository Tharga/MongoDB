using System;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;

namespace Tharga.MongoDB.Lockable.Renewable;

/// <summary>
/// Renewable variant of <see cref="LockScope{T}"/>. The commit decision (<see cref="CommitMode.Update"/>
/// vs <see cref="CommitMode.Delete"/>) is still taken at commit time; in addition the lease can be
/// renewed via <c>ExtendAsync</c> / <c>StartKeepAlive</c>, and <c>LockLost</c> is
/// cancelled if a renewal discovers the lock has been lost.
/// </summary>
public record RenewableLockScope<T> : RenewableLockScope<T, ObjectId>
    where T : LockableEntityBase<ObjectId>
{
    internal RenewableLockScope(T entity, Func<T, CommitMode?, Exception, Task> releaseAction, RenewalController controller)
        : base(entity, releaseAction, controller)
    {
    }
}

/// <summary>
/// Renewable variant of <see cref="LockScope{T, TKey}"/>. The commit decision (<see cref="CommitMode.Update"/>
/// vs <see cref="CommitMode.Delete"/>) is still taken at commit time; in addition the lease can be
/// renewed via <c>ExtendAsync</c> / <c>StartKeepAlive</c>, and <c>LockLost</c> is
/// cancelled if a renewal discovers the lock has been lost.
/// </summary>
public record RenewableLockScope<T, TKey> : LockScope<T, TKey>
    where T : LockableEntityBase<TKey>
{
    private readonly RenewalController _controller;

    internal RenewableLockScope(T entity, Func<T, CommitMode?, Exception, Task> releaseAction, RenewalController controller)
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
