using System;
using System.Threading.Tasks;
using MongoDB.Bson;

namespace Tharga.MongoDB.Lockable;

/// <summary>
/// Scope returned by <c>ILockableRepositoryCollection&lt;TEntity, TKey&gt;.LockAsync(...)</c>.
/// Unlike <see cref="EntityScope{TEntity, TKey}"/>, the commit decision (<see cref="CommitMode.Update"/> vs
/// <see cref="CommitMode.Delete"/>) is taken at commit time, not at lock time.
/// </summary>
public record LockScope<T> : LockScope<T, ObjectId>
    where T : LockableEntityBase<ObjectId>
{
    internal LockScope(T entity, Func<T, CommitMode?, Exception, Task> releaseAction, Func<TimeSpan, bool, Task<LockExtensionResult>> extendAction = null)
        : base(entity, releaseAction, extendAction)
    {
    }
}

/// <summary>
/// Scope returned by <c>ILockableRepositoryCollection&lt;TEntity, TKey&gt;.LockAsync(...)</c>.
/// The commit decision (<see cref="CommitMode.Update"/> vs <see cref="CommitMode.Delete"/>) is taken at commit time,
/// not at lock time. Disposal without commit releases the lock unchanged (mirrors <see cref="EntityScope{T, TKey}"/>).
/// </summary>
public record LockScope<T, TKey> : IAsyncDisposable, IDisposable
    where T : LockableEntityBase<TKey>
{
    private readonly Func<T, CommitMode?, Exception, Task> _releaseAction;
    private readonly Func<TimeSpan, bool, Task<LockExtensionResult>> _extendAction;
    private readonly T _entity;
    private bool _released;
    private readonly TKey _originalId;

    internal LockScope(T entity, Func<T, CommitMode?, Exception, Task> releaseAction, Func<TimeSpan, bool, Task<LockExtensionResult>> extendAction = null)
    {
        _releaseAction = releaseAction;
        _extendAction = extendAction;
        _entity = entity;
        _originalId = entity.Id;
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
    /// <exception cref="LockAlreadyReleasedException">The scope has already been committed, abandoned, or set to an error state.</exception>
    /// <exception cref="LockExpiredException">The lock is no longer held (expired under strict TTL, or re-acquired by another actor / released / removed).</exception>
    public Task<LockExtensionResult> ExtendLockAsync(TimeSpan extension, bool force = false)
    {
        if (extension <= TimeSpan.Zero) throw new ArgumentException($"{nameof(extension)} must be greater than zero. Provided value is {extension}.", nameof(extension));
        if (_released) throw new LockAlreadyReleasedException("Entity has already been released.");
        if (_extendAction == null) throw new InvalidOperationException("This lock scope does not support extending the lock.");
        return _extendAction.Invoke(extension, force);
    }

    /// <summary>
    /// Apply the chosen <paramref name="mode"/> and release the lock.
    /// </summary>
    /// <param name="mode"><see cref="CommitMode.Update"/> writes <paramref name="updatedEntity"/> (or the originally locked entity if null) and clears the lock; <see cref="CommitMode.Delete"/> deletes the document.</param>
    /// <param name="updatedEntity">For <see cref="CommitMode.Update"/>, the new entity state. Ignored for <see cref="CommitMode.Delete"/>. If null on Update, the originally locked entity is committed unchanged.</param>
    public async Task<T> CommitAsync(CommitMode mode, T updatedEntity = null)
    {
        try
        {
            var entity = updatedEntity ?? _entity;
            await Release(entity, mode, null);
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

    /// <summary>
    /// Releases the lock without any changes.
    /// </summary>
    public Task AbandonAsync()
    {
        return Release(_entity, mode: null, exception: null);
    }

    /// <summary>
    /// Releases the lock and records an exception state on it (consistent with <see cref="EntityScope{T, TKey}.SetErrorStateAsync"/>).
    /// </summary>
    public Task SetErrorStateAsync(Exception exception)
    {
        return Release(_entity, mode: null, exception: exception);
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

    private async Task Release(T updatedEntity, CommitMode? mode, Exception exception)
    {
        if (_released) throw new LockAlreadyReleasedException("Entity has already been released.");
        if (!updatedEntity.Id.Equals(_originalId))
            throw new UnlockDifferentEntityException($"Cannot release entity with different id. Original was '{_entity.Id}', releasing {updatedEntity.Id}.");
        try
        {
            await _releaseAction.Invoke(updatedEntity, mode, exception);
        }
        finally
        {
            _released = true;
        }
    }
}
