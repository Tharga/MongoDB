using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Tharga.MongoDB.Lockable;

public record EntityScope<T, TKey> : IAsyncDisposable
    where T : LockableEntityBase<TKey>
{
    private readonly Stopwatch _stopwatch = new();
    private readonly Func<T, Exception, Task> _releaseAction;
    private readonly T _entity;
    private bool _released;
    private readonly TKey _originalId;

    internal EntityScope(T entity, Func<T, Exception, Task> releaseAction)
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

    public async Task AbandonAsync()
    {
        await Release(_entity, null);
    }

    public async Task SetErrorStateAsync(Exception exception)
    {
        await Release(_entity, exception);
    }

    public async Task<T> CommitAsync(T updatedEntity = default)
    {
        var entity = updatedEntity ?? _entity;
        await Release(entity, null);
        return entity;
    }

    private async Task Release(T updatedEntity, Exception exception)
    {
        if (_released) throw new LockAlreadyReleasedException("Entity has already been released.");
        if (!updatedEntity.Id.Equals(_originalId)) throw new UnlockDifferentEntityException($"Cannot release entity with different id. Original was '{_entity.Id}', releasing {updatedEntity.Id}.");
        await _releaseAction.Invoke(updatedEntity, exception);
        _released = true;
    }
}