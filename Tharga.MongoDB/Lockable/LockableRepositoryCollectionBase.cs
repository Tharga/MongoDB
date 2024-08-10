using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Tharga.MongoDB.Disk;
using Tharga.MongoDB.Internals;

namespace Tharga.MongoDB.Lockable;

public class LockableRepositoryCollectionBase<TEntity, TKey> : DiskRepositoryCollectionBase<TEntity, TKey>, ILockableRepositoryCollection<TEntity, TKey>
    where TEntity : LockableEntityBase<TKey>
{
    private const int UnlockCounterLimit = 3; //TODO: Configurable?

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

    public override IAsyncEnumerable<TTarget> AggregateAsync<TTarget>(FilterDefinition<TEntity> filter, EPrecision precision, AggregateOperations<TTarget> operations, CancellationToken cancellationToken = default)
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

    public async ValueTask<EntityScope<TEntity, TKey>> PickForUpdate(TKey id, TimeSpan? timeout, string actor)
    {
        //TODO: Also return locked items that have expired, but that does not have an error attached.
        var filter = Builders<TEntity>.Filter.And(
            Builders<TEntity>.Filter.Eq(x => x.Id, id),
            Builders<TEntity>.Filter.Eq(x => x.Lock, null));

        var defaultTimeout = TimeSpan.FromSeconds(30); //TODO: Make this configurable on collection level.

        var lockTime = DateTime.UtcNow;
        var lockKey = Guid.NewGuid();
        actor = actor.NullIfEmpty();

        var entityLock = new Lock
        {
            LockKey = lockKey,
            LockTime = lockTime,
            ExpireTime = lockTime.Add(timeout ?? defaultTimeout),
            Actor = actor,
            ExceptionInfo = default,
        };

        var update = new UpdateDefinitionBuilder<TEntity>().Set(x => x.Lock, entityLock);

        var result = await base.UpdateOneAsync(filter, update);
        if (result.Before == null)
        {
            //Document is missing or is already locked
            var doc = await GetOneAsync(x => x.Id.Equals(id));
            if (doc == null) throw new InvalidOperationException($"Cannot find entity with id '{id}'.");
            if (doc.Lock == null) throw new InvalidOperationException("Strange behaviour, no filter match, but no lock object. Perhaps it was just released.");
            if (doc.Lock.ExceptionInfo != null) throw new InvalidOperationException($"Entity with id '{id}' has an exception attached.");
            if (doc.Lock.ExceptionInfo == null) throw new InvalidOperationException($"Entity with id '{id}' is locked.");
            throw new InvalidOperationException($"Entity with id '{id}' has an unknown state.");
        }

        return new EntityScope<TEntity, TKey>(result.Before, (entity, exception) =>
        {
            try
            {
                return ReleaseAsync(entity, entityLock, exception);
            }
            catch (Exception e)
            {
                Debugger.Break();
                var errorEntity = entity with
                {
                    Lock = entity.Lock with
                    {
                        ExceptionInfo = new ExceptionInfo
                        {
                            Type = e.GetType().Name,
                            Message = $"Failed to release lock. {e.Message}",
                            StackTrace = e.StackTrace,
                        },
                        ExpireTime = DateTime.MaxValue
                    }
                };
                _ = base.ReplaceOneAsync(errorEntity);
                _logger.LogError(e, e.Message);
                return Task.FromResult(false);
            }
        });
    }

    //public ValueTask<EntityScope<TEntity, TKey>> WaitForUpdate(ObjectId documentId, TimeSpan? timeout = default, string actor = default)
    //{
    //    //TODO: Wait for the entity to be released, then take a lock for the item to be updated.
    //    throw new NotImplementedException();
    //}

    //TODO: List documents with errors
    //TODO: List locked documents

    private async Task<bool> ReleaseAsync(TEntity entity, Lock entityLock, Exception exception /*bool externalUnlock, bool resetUnlockCounter*/)
    {
        var lockTime = DateTime.UtcNow - entityLock.ExpireTime;
        var timeout = entityLock.ExpireTime - entityLock.LockTime;
        var lockInfo = BuildLockInfo(entity, entityLock, exception, false, false);

        //if (!externalUnlock && lockTime > timeout)
        //{
        //    _logger.LogWarning("Document was locked by '{actor}' for {elapsed} instead of {timeout}.", lockInfo.DocumentLock.Actor, lockTime, timeout);
        //}

        //var collection = GetDocumentRepositoryCollection(pipelineContext);
        var updatedEntity = entity with
        {
            //Lock = lockInfo.DocumentLock,
            //UnlockCounter = lockInfo.UnlockCounter
        };

        //TODO: Should use a filter to replace (not just use the ID-value)
        //var filter = Builders<Entities.DistillerDocument>.Filter.And(
        //    Builders<Entities.DistillerDocument>.Filter.Eq(x => x.Id, document.Id),
        //    Builders<Entities.DistillerDocument>.Filter.Ne(x => x.DocumentLock, null),
        //    Builders<Entities.DistillerDocument>.Filter.Eq(x => x.DocumentLock.LockKey, documentLock.LockKey)
        //);
        var result = await ReplaceOneAsync(updatedEntity);

        if (result.Before == null) throw new InvalidOperationException("Cannot find entity before release."); //.AddData("context", pipelineContext).AddData("documentId", document.Id);
        if (result.Before.Lock == null) throw new InvalidOperationException("No lock information for document before release."); //.AddData("context", pipelineContext).AddData("documentId", document.Id);

        var after = await result.GetAfterAsync();
        if (after == null) throw new InvalidOperationException("After is null.");

        //NotifyAggregator(pipelineContext, result, after);

        return after.Lock == null;
    }

    private static (Lock EntityLock, int UnlockCounter) BuildLockInfo(TEntity entity, Lock entityLock, Exception exception, bool externalUnlock, bool resetUnlockCounter)
    {
        if (externalUnlock)
        {
            var failMessage = $"Give up unlock after {entity.UnlockCounter + 1} tries. {entityLock.ExceptionInfo?.Message}".TrimEnd();
            return (resetUnlockCounter || entity.UnlockCounter < UnlockCounterLimit
                ? null
                : entityLock with
                {
                    ExceptionInfo = entityLock.ExceptionInfo == null
                        ? new ExceptionInfo
                        {
                            Type = null,
                            Message = failMessage,
                            StackTrace = null
                        }
                        : entityLock.ExceptionInfo with
                        {
                            Message = failMessage
                        },
                    ExpireTime = DateTime.MaxValue
                }, entity.UnlockCounter + 1);
        }

        if (exception != null)
        {
            return (entityLock with { ExceptionInfo = new ExceptionInfo
            {
                Type = exception.GetType().Name,
                Message = exception.Message,
                StackTrace = exception.StackTrace,
            } }, entity.UnlockCounter);
        }

        return (null, 0);
    }
}