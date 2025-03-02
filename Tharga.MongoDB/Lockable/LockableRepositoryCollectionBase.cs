using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Tharga.MongoDB.Disk;
using Tharga.MongoDB.Internals;

namespace Tharga.MongoDB.Lockable;

public class LockableRepositoryCollectionBase<TEntity, TKey> : DiskRepositoryCollectionBase<TEntity, TKey>, ILockableRepositoryCollection<TEntity, TKey>
    where TEntity : LockableEntityBase<TKey>
{
    protected virtual int UnlockCounterLimit { get; init; } = 3;
    protected virtual TimeSpan DefaultTimeout { get; init; } = TimeSpan.FromSeconds(30);

    private static readonly AutoResetEvent _releaseEvent = new(false);

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

    public override async Task<EntityChangeResult<TEntity>> AddOrReplaceAsync(TEntity entity)
    {
        if (await TryAddAsync(entity))
        {
            return new EntityChangeResult<TEntity>(default, entity);
        }

        throw new NotSupportedException(BuildErrorMessage());
    }

    public override Task<EntityChangeResult<TEntity>> ReplaceOneAsync(TEntity entity, OneOption<TEntity> options = null)
    {
        throw new NotSupportedException(BuildErrorMessage());
    }

    public override Task<EntityChangeResult<TEntity>> ReplaceOneAsync(TEntity entity, FilterDefinition<TEntity> filter, OneOption<TEntity> options = null)
    {
        throw new NotSupportedException(BuildErrorMessage());
    }

    public override Task<long> UpdateAsync(FilterDefinition<TEntity> filter, UpdateDefinition<TEntity> update)
    {
        throw new NotSupportedException(BuildErrorMessage());
    }

    public override Task<EntityChangeResult<TEntity>> UpdateOneAsync(TKey id, UpdateDefinition<TEntity> update)
    {
        throw new NotSupportedException(BuildErrorMessage());
    }

    public override Task<EntityChangeResult<TEntity>> UpdateOneAsync(FilterDefinition<TEntity> filter, UpdateDefinition<TEntity> update, OneOption<TEntity> options = null)
    {
        throw new NotSupportedException(BuildErrorMessage());
    }

    private string BuildErrorMessage()
    {
        return $"Use {nameof(PickForUpdateAsync)} to get an update {nameof(EntityScope<TEntity, TKey>)} that can be used for update.";
    }

    public async Task<EntityScope<TEntity, TKey>> PickForUpdateAsync(TKey id, TimeSpan? timeout = default, string actor = default)
    {
        var result = await GetForUpdateAsync(Builders<TEntity>.Filter.Eq(x => x.Id, id), timeout, actor);
        if (!string.IsNullOrEmpty(result.ErrorMessage))
        {
            throw new InvalidOperationException(result.ErrorMessage);
        }

        return result.EntityScope;
    }

    public async Task<EntityScope<TEntity, TKey>> WaitForUpdateAsync(TKey id, TimeSpan? timeout = default, string actor = default, CancellationToken cancellationToken = default)
    {
        var actualTimeout = timeout ?? DefaultTimeout;
        var recheckTimeInterval = actualTimeout / 5;
        using var timeoutCts = new CancellationTokenSource(actualTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        var waitHandles = new[] { _releaseEvent, linkedCts.Token.WaitHandle };

        while (!linkedCts.Token.IsCancellationRequested)
        {
            var result = await GetForUpdateAsync(Builders<TEntity>.Filter.Eq(x => x.Id, id), timeout, actor);
            if (!result.ShouldWait) return HandleFinalResult(result);
            WaitHandle.WaitAny(waitHandles, recheckTimeInterval);
        }

        if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException("The operation was canceled.");

        var finalCheckResult = await GetForUpdateAsync(Builders<TEntity>.Filter.Eq(x => x.Id, id), timeout, actor);
        if (!finalCheckResult.ShouldWait) return HandleFinalResult(finalCheckResult);

        throw new TimeoutException("No valid entity has been released for update.");

        EntityScope<TEntity, TKey> HandleFinalResult((EntityScope<TEntity, TKey> EntityScope, string ErrorMessage, bool ShouldWait) result2)
        {
            if (!string.IsNullOrEmpty(result2.ErrorMessage)) throw new InvalidOperationException(result2.ErrorMessage);
            return result2.EntityScope;
        }
    }

    private async Task<(EntityScope<TEntity, TKey> EntityScope, string ErrorMessage, bool ShouldWait)> GetForUpdateAsync(FilterDefinition<TEntity> filter, TimeSpan? timeout = default, string actor = default)
    {
        var lockTime = DateTime.UtcNow;
        var lockKey = Guid.NewGuid();
        actor = actor.NullIfEmpty();

        var unlockedFilter = Builders<TEntity>.Filter.And(
            filter,
            Builders<TEntity>.Filter.Eq(x => x.Lock, null)
        );
        var expiredLockFilter = Builders<TEntity>.Filter.And(
            filter,
            Builders<TEntity>.Filter.Ne(x => x.Lock, null),
            Builders<TEntity>.Filter.Eq(x => x.Lock.ExceptionInfo, null),
            Builders<TEntity>.Filter.Lte(x => x.Lock.ExpireTime, lockTime)
        );
        var matchFilter = Builders<TEntity>.Filter.Or(unlockedFilter, expiredLockFilter);

        var entityLock = new Lock
        {
            LockKey = lockKey,
            LockTime = lockTime,
            ExpireTime = lockTime.Add(timeout ?? DefaultTimeout),
            Actor = actor,
            ExceptionInfo = default,
        };

        var update = new UpdateDefinitionBuilder<TEntity>().Set(x => x.Lock, entityLock);

        var result = await base.UpdateOneAsync(matchFilter, update);
        if (result.Before == null)
        {
            //Document is missing or is already locked
            var docs = await GetAsync(filter).ToArrayAsync();

            //if (!docs.Any()) return (default, "Cannot find entity that matches the filter.", false);
            if (!docs.Any()) return (default, null, false);

            if (docs.Length == 1)
            {
                var doc = docs.Single();
                if (doc.Lock?.ExceptionInfo == null)
                {
                    var timeString = doc.Lock == null ? null : $" for {(doc.Lock.ExpireTime ?? (doc.Lock.LockTime + DefaultTimeout)) - DateTime.UtcNow}";
                    var actorString = doc.Lock?.Actor == null ? null : $" by '{doc.Lock.Actor}'";
                    return (default, $"Entity with id '{doc.Id}' is locked{actorString}{timeString}.", true);
                }

                if (doc.Lock.ExceptionInfo != null) return (default, $"Entity with id '{doc.Id}' has an exception attached.", false);
                return (default, $"Entity with id '{doc.Id}' has an unknown state.", false);
            }
            else
            {
                throw new NotImplementedException("Messages for documents with muliple matches but no hit, has not yet been implemented.");
            }
        }

        return (new EntityScope<TEntity, TKey>(result.Before, ReleaseEntity(entityLock)), null, false);
    }

    private Func<TEntity, Exception, Task> ReleaseEntity(Lock entityLock)
    {
        return (entity, exception) =>
        {
            try
            {
                return ReleaseAsync(entity, entityLock, exception, false);
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
            finally
            {
                _releaseEvent.Set();
            }
        };
    }

    private async Task<bool> ReleaseAsync(TEntity entity, Lock entityLock, Exception exception, bool externalUnlock /*bool resetUnlockCounter*/)
    {
        var lockTime = DateTime.UtcNow - entityLock.ExpireTime;
        var timeout = entityLock.ExpireTime - entityLock.LockTime;
        var lockInfo = BuildLockInfo(entity, entityLock, exception, false, false);

        if (!externalUnlock && lockTime > timeout)
        {
            throw new LockExpiredException($"Entity was locked for {lockTime} instead of {timeout}.");
        }

        var updatedEntity = entity with
        {
            Lock = lockInfo.EntityLock,
            UnlockCounter = lockInfo.UnlockCounter
        };

        //NOTE: This filter assures that the correct lock still exists on the entity.
        var filter = Builders<TEntity>.Filter.And(
            Builders<TEntity>.Filter.Eq(x => x.Id, entity.Id),
            Builders<TEntity>.Filter.Ne(x => x.Lock, null),
            Builders<TEntity>.Filter.Eq(x => x.Lock.LockKey, entityLock.LockKey)
        );
        var result = await base.ReplaceOneAsync(updatedEntity, filter);

        if (result.Before == null) throw new InvalidOperationException("Cannot find entity before release."); //.AddData("context", pipelineContext).AddData("documentId", document.Id);
        if (result.Before.Lock == null) throw new InvalidOperationException("No lock information for document before release."); //.AddData("context", pipelineContext).AddData("documentId", document.Id);

        var after = await result.GetAfterAsync();
        if (after == null) throw new InvalidOperationException("After is null.");

        return after.Lock == null;
    }

    private (Lock EntityLock, int UnlockCounter) BuildLockInfo(TEntity entity, Lock entityLock, Exception exception, bool externalUnlock, bool resetUnlockCounter)
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
            return (entityLock with
            {
                ExceptionInfo = new ExceptionInfo
                {
                    Type = exception.GetType().Name,
                    Message = exception.Message,
                    StackTrace = exception.StackTrace
                }
            }, entity.UnlockCounter);
        }

        return (null, 0);
    }
}

public class LockableRepositoryCollectionBase<TEntity> : LockableRepositoryCollectionBase<TEntity, ObjectId>
    where TEntity : LockableEntityBase
{
    protected LockableRepositoryCollectionBase(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<LockableRepositoryCollectionBase<TEntity, ObjectId>> logger, DatabaseContext databaseContext)
        : base(mongoDbServiceFactory, logger, databaseContext)
    {
    }
}