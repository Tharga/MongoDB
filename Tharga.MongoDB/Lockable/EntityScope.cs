using System;
using System.Diagnostics;
using System.Threading.Tasks;
using MongoDB.Bson;

namespace Tharga.MongoDB.Lockable;

public record EntityScope<T> : EntityScope<T, ObjectId>
    where T : LockableEntityBase<ObjectId>
{
    internal EntityScope(T entity, Func<T, bool, Exception, Task> releaseAction, Func<TimeSpan, bool, Task<LockExtensionResult>> extendAction = null)
        : base(entity, releaseAction, extendAction)
    {
    }
}

public record EntityScope<T, TKey> : IAsyncDisposable, IDisposable
    where T : LockableEntityBase<TKey>
{
    private readonly Stopwatch _stopwatch = new();
    private readonly Func<T, bool, Exception, Task> _releaseAction;
    private readonly Func<TimeSpan, bool, Task<LockExtensionResult>> _extendAction;
    private readonly T _entity;
    private bool _released;
    private readonly TKey _originalId;

    internal EntityScope(T entity, Func<T, bool, Exception, Task> releaseAction, Func<TimeSpan, bool, Task<LockExtensionResult>> extendAction = null)
    {
        _stopwatch.Start();
        _releaseAction = releaseAction;
        _extendAction = extendAction;
        _entity = entity;
        _originalId = _entity.Id;
    }

    public T Entity => _entity;

    /// <summary>
    /// Extends the lock — "buys more time" by setting its expiry to <c>UtcNow + <paramref name="extension"/></c>.
    /// Safe to call frequently (e.g. inside a long or irregular loop): an actual database write happens at most
    /// once per the collection's minimum extend interval (default 60s). Calls inside that window are in-memory
    /// no-ops; the first call at or after the window writes immediately.
    /// </summary>
    /// <param name="extension">How long from now the lock should remain held when a write happens. Must be greater than zero.</param>
    /// <param name="force">When <c>true</c>, bypass the throttle and write immediately (still expiry/lock-key gated).</param>
    /// <returns>The current lock expiry and whether this call actually wrote (<see cref="LockExtensionResult.Extended"/>).</returns>
    /// <exception cref="LockAlreadyReleasedException">The scope has already been committed or abandoned.</exception>
    /// <exception cref="LockExpiredException">The lock is no longer held (expired under strict TTL, or re-acquired by another actor / released / removed).</exception>
    public Task<LockExtensionResult> ExtendLockAsync(TimeSpan extension, bool force = false)
    {
        if (extension <= TimeSpan.Zero) throw new ArgumentException($"{nameof(extension)} must be greater than zero. Provided value is {extension}.", nameof(extension));
        if (_released) throw new LockAlreadyReleasedException("Entity has already been released.");
        if (_extendAction == null) throw new InvalidOperationException("This lock scope does not support extending the lock.");
        return _extendAction.Invoke(extension, force);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_released)
        {
            await AbandonAsync();
        }
    }

    public void Dispose()
    {
        if (!_released)
        {
            Task.Run(AbandonAsync);
        }
    }

    /// <summary>
    /// Releases the lock without any changes to the entity.
    /// </summary>
    /// <returns></returns>
    public async Task AbandonAsync()
    {
        await Release(_entity, false, null);
    }

    /// <summary>
    /// Sets an exception on the lock.
    /// </summary>
    /// <param name="exception"></param>
    /// <returns></returns>
    public async Task SetErrorStateAsync(Exception exception)
    {
        await Release(_entity, false, exception);
    }

    /// <summary>
    /// Saves updates back to the database and release the lock.
    /// </summary>
    /// <param name="updatedEntity"></param>
    /// <returns></returns>
    public async Task<T> CommitAsync(T updatedEntity = null)
    {
        try
        {
            var entity = updatedEntity ?? _entity;
            await Release(entity, true, null);
            return entity;
        }
        catch (UnlockDifferentEntityException)
        {
            throw;
        }
        catch (LockAlreadyReleasedException)
        {
            throw;
        }
        catch (LockExpiredException)
        {
            throw;
        }
        catch (Exception e)
        {
            throw new CommitException(e);
        }
    }

    private async Task Release(T updatedEntity, bool commit, Exception exception)
    {
        if (_released) throw new LockAlreadyReleasedException("Entity has already been released.");
        if (!updatedEntity.Id.Equals(_originalId)) throw new UnlockDifferentEntityException($"Cannot release entity with different id. Original was '{_entity.Id}', releasing {updatedEntity.Id}.");
        try
        {
            await _releaseAction.Invoke(updatedEntity, commit, exception);
        }
        finally
        {
            _released = true;
        }
    }
}