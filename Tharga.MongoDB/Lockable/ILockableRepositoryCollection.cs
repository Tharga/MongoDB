using System.Threading.Tasks;
using System;
using MongoDB.Bson;

namespace Tharga.MongoDB.Lockable;

public interface ILockableRepositoryCollection<TEntity, TKey> : IReadOnlyRepositoryCollection<TEntity, TKey>
    where TEntity : LockableEntityBase<TKey>
{
    //IAsyncEnumerable<Entities.DistillerDocument> GetDocumentsAsync(PipelineContext pipelineContext, Expression<Func<Entities.DistillerDocument, bool>> expression);
    //Task<Entities.DistillerDocument> GetDocumentAsync(PipelineContext pipelineContext, Expression<Func<Entities.DistillerDocument, bool>> expression);
    //ValueTask<EntityScope<Entities.DistillerDocument>> PickForUpdate(PipelineContext pipelineContext, ObjectId documentId, IActor actor, TimeSpan timeout);
    Task<EntityScope<TEntity, TKey>> PickForUpdate(TKey id, TimeSpan? timeout = default, string actor = default);
    //ValueTask<EntityScope<TEntity, TKey>> WaitForUpdate(ObjectId documentId, TimeSpan? timeout = default, string actor = default);
    //Task AddOrReplaceDocumentAsync(PipelineContext pipelineContext, Entities.DistillerDocument document);
    //Task<bool> UnlockDocumentAsync(PipelineContext pipelineContext, ObjectId documentId, bool resetUnlockCounter = false);
    //Task DeleteAllDocumentsAsync(PipelineContext pipelineContext);
    //Task<DistillerDocumentCount> GetCountAsync(PipelineContext pipelineContext);
    //Task<int> BatchMoveAsync(PipelineContext pipelineContext, Expression<Func<Entities.DistillerDocument, bool>> expression, DistillerDocumentState targetState);
}