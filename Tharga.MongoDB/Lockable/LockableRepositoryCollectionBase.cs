using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Tharga.MongoDB.Disk;

namespace Tharga.MongoDB.Lockable;

public class LockableRepositoryCollectionBase<TEntity, TKey> : DiskRepositoryCollectionBase<TEntity, TKey>, ILockableRepositoryCollection<TEntity, TKey>
    where TEntity : LockableEntityBase<TKey>
{
    /// <summary>
    /// Override this constructor for static collections.
    /// </summary>
    /// <param name="mongoDbServiceFactory"></param>
    /// <param name="logger"></param>
    protected LockableRepositoryCollectionBase(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<RepositoryCollectionBase<TEntity, TKey>> logger = null)
        : base(mongoDbServiceFactory, logger)
    {
    }

    /// <summary>
    /// Use this constructor for dynamic collections together with ICollectionProvider.
    /// </summary>
    /// <param name="mongoDbServiceFactory"></param>
    /// <param name="logger"></param>
    /// <param name="databaseContext"></param>
    protected LockableRepositoryCollectionBase(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<RepositoryCollectionBase<TEntity, TKey>> logger, DatabaseContext databaseContext)
        : base(mongoDbServiceFactory, logger, databaseContext)
    {
    }

    public override IAsyncEnumerable<TTarget> AggregateAsync<TTarget>(FilterDefinition<TEntity> filter, EPrecision precision, AggregateOperations<TTarget> operations,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public override Task<EntityChangeResult<TEntity>> AddOrReplaceAsync(TEntity entity)
    {
        //TODO: Check if an entity can be added. But do not allow updates.
        throw new NotSupportedException($"Use {nameof(PickForUpdate)} to get an update {nameof(EntityScope<TEntity, TKey>)} that can be used for update.");
    }

    public override Task<EntityChangeResult<TEntity>> ReplaceOneAsync(TEntity entity, OneOption<TEntity> options = null)
    {
        //TODO: Do not for locked entities
        throw new NotImplementedException();
    }

    public override Task<long> UpdateAsync(FilterDefinition<TEntity> filter, UpdateDefinition<TEntity> update)
    {
        //TODO: Do not for locked entities
        throw new NotImplementedException();
    }

    public override Task<EntityChangeResult<TEntity>> UpdateOneAsync(TKey id, UpdateDefinition<TEntity> update)
    {
        //TODO: Do not for locked entities
        throw new NotImplementedException();
    }

    public override Task<EntityChangeResult<TEntity>> UpdateOneAsync(FilterDefinition<TEntity> filter, UpdateDefinition<TEntity> update, FindOneAndUpdateOptions<TEntity> options)
    {
        //TODO: Do not for locked entities
        throw new NotImplementedException();
    }

    public override Task<EntityChangeResult<TEntity>> UpdateOneAsync(FilterDefinition<TEntity> filter, UpdateDefinition<TEntity> update, OneOption<TEntity> options = null)
    {
        //TODO: Do not for locked entities
        throw new NotImplementedException();
    }

    public override Task<TEntity> DeleteOneAsync(TKey id)
    {
        //TODO: Do not for locked entities
        throw new NotImplementedException();
    }

    public override Task<TEntity> DeleteOneAsync(Expression<Func<TEntity, bool>> predicate, FindOneAndDeleteOptions<TEntity, TEntity> options)
    {
        //TODO: Do not for locked entities
        throw new NotImplementedException();
    }

    public override Task<TEntity> DeleteOneAsync(Expression<Func<TEntity, bool>> predicate = null, OneOption<TEntity> options = null)
    {
        //TODO: Do not for locked entities
        throw new NotImplementedException();
    }

    public override Task<long> DeleteManyAsync(Expression<Func<TEntity, bool>> predicate)
    {
        //TODO: Do not for locked entities
        throw new NotImplementedException();
    }

    public ValueTask<EntityScope<TEntity, TKey>> PickForUpdate(ObjectId documentId, TimeSpan? timeout, string actor)
    {
        //var filter = Builders<Entities.DistillerDocument>.Filter.And(
        //    Builders<Entities.DistillerDocument>.Filter.Eq(x => x.Id, documentId),
        //    Builders<Entities.DistillerDocument>.Filter.Eq(x => x.DocumentLock, null));

        //var infoTime = DateTime.UtcNow;
        //var documentLock = new Entities.DocumentLock { LockKey = Guid.NewGuid(), InfoTime = infoTime, Actor = actor.Name, ExceptionInfo = null, ExpireTime = infoTime.Add(timeout) };
        //var update = new UpdateDefinitionBuilder<Entities.DistillerDocument>().Set(x => x.DocumentLock, documentLock);

        //var collection = GetDocumentRepositoryCollection(pipelineContext);
        //var result = await collection.UpdateOneAsync(filter, update);
        //if (result.Before == null)
        //{
        //    //Document is missing or is already locked
        //    var doc = await collection.GetOneAsync(x => x.Id == documentId);
        //    if (doc == null) return null;
        //    if (doc.DocumentLock?.ExceptionInfo != null) return null;

        //    throw new NotImplementedException($"Retry '{nameof(PickForUpdate)}' has not yet been implemented.");
        //}

        //return new EntityScope<Entities.DistillerDocument>(result.Before, (d, exception) =>
        //{
        //    try
        //    {
        //        return ReleaseAsync(pipelineContext, d, exception, documentLock, false, false);
        //    }
        //    catch (Exception e)
        //    {
        //        Debugger.Break();
        //        var doc = d with
        //        {
        //            DocumentLock = new DocumentLock { LockKey = Guid.NewGuid(), InfoTime = DateTime.UtcNow, Actor = actor.Name, ExceptionInfo = new DocumentLock.ExceptionData { Type = e.GetType().Name, Message = e.Message, StackTrace = e.StackTrace }, ExpireTime = infoTime.Add(timeout) }
        //        };
        //        collection.ReplaceOneAsync(doc);
        //        _logger.LogError(e, e.Message);
        //        return Task.FromResult(false);
        //    }
        //});
        throw new NotImplementedException();
    }

    public ValueTask<EntityScope<TEntity, TKey>> WaitForUpdate(ObjectId documentId, TimeSpan? timeout = default, string actor = default)
    {
        //TODO: Wait for the entity to be released, then take a lock for the item to be updated.
        throw new NotImplementedException();
    }
}