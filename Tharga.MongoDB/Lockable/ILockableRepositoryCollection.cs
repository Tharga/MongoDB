using System.Threading.Tasks;
using System;
using System.Diagnostics;
using MongoDB.Bson;
using System.Numerics;

namespace Tharga.MongoDB.Lockable;

public interface ILockableRepositoryCollection<TEntity, TKey> : IReadOnlyRepositoryCollection<TEntity, TKey>
    where TEntity : LockableEntityBase<TKey>
{
    //IAsyncEnumerable<Entities.DistillerDocument> GetDocumentsAsync(PipelineContext pipelineContext, Expression<Func<Entities.DistillerDocument, bool>> expression);
    //Task<Entities.DistillerDocument> GetDocumentAsync(PipelineContext pipelineContext, Expression<Func<Entities.DistillerDocument, bool>> expression);
    //ValueTask<EntityScope<Entities.DistillerDocument>> PickForUpdate(PipelineContext pipelineContext, ObjectId documentId, IActor actor, TimeSpan timeout);
    ValueTask<EntityScope<TEntity, TKey>> PickForUpdate(ObjectId documentId, TimeSpan? timeout = default, string actor = default);
    ValueTask<EntityScope<TEntity, TKey>> WaitForUpdate(ObjectId documentId, TimeSpan? timeout = default, string actor = default);
    //Task AddOrReplaceDocumentAsync(PipelineContext pipelineContext, Entities.DistillerDocument document);
    //Task<bool> UnlockDocumentAsync(PipelineContext pipelineContext, ObjectId documentId, bool resetUnlockCounter = false);
    //Task DeleteAllDocumentsAsync(PipelineContext pipelineContext);
    //Task<DistillerDocumentCount> GetCountAsync(PipelineContext pipelineContext);
    //Task<int> BatchMoveAsync(PipelineContext pipelineContext, Expression<Func<Entities.DistillerDocument, bool>> expression, DistillerDocumentState targetState);
}

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
        if (_released) throw new InvalidOperationException("Entity has already been released.");
        if (!updatedEntity.Id.Equals(_original.Id)) throw new InvalidOperationException($"Cannot commit entity with different id. Original was '{_original.Id}', releasing {updatedEntity.Id}.");
        await _releaseAction.Invoke(updatedEntity, exception);
        _released = true;
    }
}