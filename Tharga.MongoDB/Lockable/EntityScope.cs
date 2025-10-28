using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Tharga.MongoDB.Lockable;

public record EntityScope<T, TKey> : IAsyncDisposable, IDisposable
    where T : LockableEntityBase<TKey>
{
    private readonly Stopwatch _stopwatch = new();
    private readonly Func<T, bool, Exception, Task> _releaseAction;
    private readonly T _entity;
    private bool _released;
    private readonly TKey _originalId;

    internal EntityScope(T entity, Func<T, bool, Exception, Task> releaseAction)
    {
        _stopwatch.Start();
        _releaseAction = releaseAction;
        _entity = entity;
        _originalId = _entity.Id;
    }

    public T Entity => _entity;

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