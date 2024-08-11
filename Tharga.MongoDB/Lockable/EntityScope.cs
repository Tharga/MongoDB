using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Tharga.MongoDB.Lockable;

public record EntityScope<T, TKey> : IAsyncDisposable
    where T : LockableEntityBase<TKey>
{
    private readonly Stopwatch _stopwatch = new();
    private readonly Func<T, Exception, Task> _releaseAction;
    private readonly T _original;
    private bool _released;

    internal EntityScope(T entity, Func<T, Exception, Task> releaseAction)
    {
        _stopwatch.Start();
        _releaseAction = releaseAction;
        _original = entity;
    }

    public T Entity => _original;

    public async ValueTask DisposeAsync()
    {
        if (!_released)
        {
            await AbandonAsync();
        }
    }

    public async Task AbandonAsync()
    {
        await Release(_original, null);
    }

    public async Task SetErrorStateAsync(Exception exception)
    {
        await Release(_original, exception);
    }

    public async Task CommitAsync(T updatedEntity)
    {
        await Release(updatedEntity, null);
    }

    private async Task Release(T updatedEntity, Exception exception)
    {
        if (_released) throw new LockAlreadyReleasedException("Entity has already been released.");
        if (!updatedEntity.Id.Equals(_original.Id)) throw new UnlockDifferentEntityException($"Cannot release entity with different id. Original was '{_original.Id}', releasing {updatedEntity.Id}.");
        await _releaseAction.Invoke(updatedEntity, exception);
        _released = true;
    }
}