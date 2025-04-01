using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Tharga.MongoDB.Disk;
using Tharga.MongoDB.Internals;

namespace Tharga.MongoDB.Lockable;

public class LockableRepositoryCollectionBase<TEntity, TKey> : RepositoryCollectionBase<TEntity, TKey>, ILockableRepositoryCollection<TEntity, TKey>
    where TEntity : LockableEntityBase<TKey>
{
    private readonly IMongoDbServiceFactory _mongoDbServiceFactory;
    private DiskRepositoryCollectionBase<TEntity, TKey> _disk;
    private static readonly AutoResetEvent _releaseEvent = new(false);

    protected LockableRepositoryCollectionBase(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<LockableRepositoryCollectionBase<TEntity, TKey>> logger = null, DatabaseContext databaseContext = null)
        : base(mongoDbServiceFactory, logger, databaseContext)
    {
        _mongoDbServiceFactory = mongoDbServiceFactory;
    }

    internal override IRepositoryCollection<TEntity, TKey> BaseCollection => Disk;
    private RepositoryCollectionBase<TEntity, TKey> Disk => _disk ??= new GenericDiskRepositoryCollection<TEntity, TKey>(_mongoDbServiceFactory, _databaseContext ?? new DatabaseContext { CollectionName = CollectionName, DatabasePart = DatabasePart, ConfigurationName = ConfigurationName }, _logger, this);

    protected virtual int UnlockCounterLimit { get; init; } = 3;
    protected virtual TimeSpan DefaultTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public override IAsyncEnumerable<TEntity> GetAsync(Expression<Func<TEntity, bool>> predicate = null, Options<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        return Disk.GetAsync(predicate, options, cancellationToken);
    }

    public override IAsyncEnumerable<TEntity> GetAsync(FilterDefinition<TEntity> filter, Options<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        return Disk.GetAsync(filter, options, cancellationToken);
    }

    public override IAsyncEnumerable<T> GetAsync<T>(Expression<Func<T, bool>> predicate = null, Options<T> options = null, CancellationToken cancellationToken = default)
    {
        return Disk.GetAsync(predicate, options, cancellationToken);
    }

    public override IAsyncEnumerable<T> GetProjectionAsync<T>(Expression<Func<T, bool>> predicate = null, Options<T> options = null, CancellationToken cancellationToken = default)
    {
        return Disk.GetProjectionAsync(predicate, options, cancellationToken);
    }

    public override Task<Result<TEntity, TKey>> QueryAsync(Expression<Func<TEntity, bool>> predicate = null, Options<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        return Disk.QueryAsync(predicate, options, cancellationToken);
    }

    public override Task<Result<TEntity, TKey>> QueryAsync(FilterDefinition<TEntity> filter, Options<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        return Disk.QueryAsync(filter, options, cancellationToken);
    }

    public override IAsyncEnumerable<ResultPage<TEntity, TKey>> GetPagesAsync(Expression<Func<TEntity, bool>> predicate = null, Options<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        return Disk.GetPagesAsync(predicate, options, cancellationToken);
    }

    public override Task<TEntity> GetOneAsync(TKey id, CancellationToken cancellationToken = default)
    {
        return Disk.GetOneAsync(id, cancellationToken);
    }

    public override Task<TEntity> GetOneAsync(Expression<Func<TEntity, bool>> predicate = null, OneOption<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        return Disk.GetOneAsync(predicate, options, cancellationToken);
    }

    public override Task<TEntity> GetOneAsync(FilterDefinition<TEntity> filter, OneOption<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        return Disk.GetOneAsync(filter, options, cancellationToken);
    }

    public override Task<T> GetOneAsync<T>(Expression<Func<T, bool>> predicate = null, OneOption<T> options = null, CancellationToken cancellationToken = default)
    {
        return Disk.GetOneAsync(predicate, options, cancellationToken);
    }

    public override Task<T> GetOneProjectionAsync<T>(Expression<Func<T, bool>> predicate = null, OneOption<T> options = null, CancellationToken cancellationToken = default)
    {
        return Disk.GetOneProjectionAsync(predicate, options, cancellationToken);
    }

    public override Task<long> CountAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        return Disk.CountAsync(predicate, cancellationToken);
    }

    public override Task<long> CountAsync(FilterDefinition<TEntity> filter, CancellationToken cancellationToken = default)
    {
        return Disk.CountAsync(filter, cancellationToken);
    }

    public override Task<long> GetSizeAsync()
    {
        return Disk.GetSizeAsync();
    }

    public override Task AddAsync(TEntity entity)
    {
        return Disk.AddAsync(entity);
    }

    public override Task<bool> TryAddAsync(TEntity entity)
    {
        return Disk.TryAddAsync(entity);
    }

    public override Task AddManyAsync(IEnumerable<TEntity> entities)
    {
        throw new NotSupportedException(BuildErrorMessage());
    }

    public override Task<EntityChangeResult<TEntity>> AddOrReplaceAsync(TEntity entity)
    {
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

    public override Task<EntityChangeResult<TEntity>> UpdateOneAsync(FilterDefinition<TEntity> filter, UpdateDefinition<TEntity> update, FindOneAndUpdateOptions<TEntity> options)
    {
        throw new NotSupportedException(BuildErrorMessage());
    }

    public override Task<EntityChangeResult<TEntity>> UpdateOneAsync(FilterDefinition<TEntity> filter, UpdateDefinition<TEntity> update, OneOption<TEntity> options = null)
    {
        throw new NotSupportedException(BuildErrorMessage());
    }

    public override Task<TEntity> DeleteOneAsync(TKey id)
    {
        throw new NotSupportedException(BuildErrorMessage());
    }

    public override Task<TEntity> DeleteOneAsync(Expression<Func<TEntity, bool>> predicate, FindOneAndDeleteOptions<TEntity, TEntity> options)
    {
        throw new NotSupportedException(BuildErrorMessage());
    }

    public override Task<TEntity> DeleteOneAsync(Expression<Func<TEntity, bool>> predicate = null, OneOption<TEntity> options = null)
    {
        throw new NotSupportedException(BuildErrorMessage());
    }

    public override async Task<long> DeleteManyAsync(Expression<Func<TEntity, bool>> predicate)
    {
        Expression<Func<TEntity, bool>> expression = x => x.Lock == null || (x.Lock != null && x.Lock.ExceptionInfo == null && (x.Lock.ExpireTime ?? (x.Lock.LockTime + DefaultTimeout)) > DateTime.UtcNow);
        return await Disk.DeleteManyAsync(predicate.AndAlso(expression));
    }

    public override IMongoCollection<TEntity> GetCollection()
    {
        return Disk.GetCollection();
    }

    public override Task DropCollectionAsync()
    {
        return Disk.DropCollectionAsync();
    }

    public async Task<EntityScope<TEntity, TKey>> PickForUpdateAsync(TKey id, TimeSpan? timeout = default, string actor = default)
    {
        var result = await GetForUpdateAsync(Builders<TEntity>.Filter.Eq(x => x.Id, id), timeout, actor, CommitMode.Update);
        if (!string.IsNullOrEmpty(result.ErrorMessage))
        {
            throw new InvalidOperationException(result.ErrorMessage);
        }

        return result.EntityScope;
    }

    public async Task<EntityScope<TEntity, TKey>> PickForDeleteAsync(TKey id, TimeSpan? timeout = default, string actor = default)
    {
        var result = await GetForUpdateAsync(Builders<TEntity>.Filter.Eq(x => x.Id, id), timeout, actor, CommitMode.Delete);
        if (!string.IsNullOrEmpty(result.ErrorMessage))
        {
            throw new InvalidOperationException(result.ErrorMessage);
        }

        return result.EntityScope;
    }

    public async Task<EntityScope<TEntity, TKey>> WaitForUpdateAsync(TKey id, TimeSpan? timeout = default, string actor = default, CancellationToken cancellationToken = default)
    {
        return await EntityScope(id, timeout, actor, cancellationToken, CommitMode.Update);
    }

    public async Task<EntityScope<TEntity, TKey>> WaitForDeleteAsync(TKey id, TimeSpan? timeout = default, string actor = default, CancellationToken cancellationToken = default)
    {
        return await EntityScope(id, timeout, actor, cancellationToken, CommitMode.Delete);
    }

    public IAsyncEnumerable<EntityLock<TEntity, TKey>> GetLockedAsync(LockMode lockMode)
    {
        switch (lockMode)
        {
            case LockMode.Locked:
                return Disk.GetAsync(x => x.Lock != null && x.Lock.ExceptionInfo == null && (x.Lock.ExpireTime ?? (x.Lock.LockTime + DefaultTimeout)) <= DateTime.UtcNow)
                    .Select(x => new EntityLock<TEntity, TKey>(x, x.Lock));
            case LockMode.Expired:
                return Disk.GetAsync(x => x.Lock != null && x.Lock.ExceptionInfo == null && (x.Lock.ExpireTime ?? (x.Lock.LockTime + DefaultTimeout)) > DateTime.UtcNow)
                    .Select(x => new EntityLock<TEntity, TKey>(x, x.Lock));
            case LockMode.Exception:
                return Disk.GetAsync(x => x.Lock != null && x.Lock.ExceptionInfo != null)
                    .Select(x => new EntityLock<TEntity, TKey>(x, x.Lock));
            default:
                throw new ArgumentOutOfRangeException(nameof(lockMode), lockMode, null);
        }
    }

    public async Task<bool> ReleaseAsync(TKey id)
    {
        var filter = Builders<TEntity>.Filter.And(
            Builders<TEntity>.Filter.Eq(x => x.Id, id),
            Builders<TEntity>.Filter.Ne(x => x.Lock, null),
            Builders<TEntity>.Filter.Ne(x => x.Lock.ExceptionInfo, null)
        );
        var update = new UpdateDefinitionBuilder<TEntity>().Set(x => x.Lock, null);
        var result = await Disk.UpdateAsync(filter, update);
        return result == 1;
    }

    private async Task<EntityScope<TEntity, TKey>> EntityScope(TKey id, TimeSpan? timeout, string actor, CancellationToken cancellationToken, CommitMode commitMode)
    {
        var actualTimeout = timeout ?? DefaultTimeout;
        var recheckTimeInterval = actualTimeout / 5;
        using var timeoutCts = new CancellationTokenSource(actualTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        var waitHandles = new[] { _releaseEvent, linkedCts.Token.WaitHandle };

        while (!linkedCts.Token.IsCancellationRequested)
        {
            var result = await GetForUpdateAsync(Builders<TEntity>.Filter.Eq(x => x.Id, id), timeout, actor, commitMode);
            if (!result.ShouldWait) return HandleFinalResult(result);
            WaitHandle.WaitAny(waitHandles, recheckTimeInterval);
        }

        if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException("The operation was canceled.");

        var finalCheckResult = await GetForUpdateAsync(Builders<TEntity>.Filter.Eq(x => x.Id, id), timeout, actor, commitMode);
        if (!finalCheckResult.ShouldWait) return HandleFinalResult(finalCheckResult);

        throw new TimeoutException("No valid entity has been released for update.");

        EntityScope<TEntity, TKey> HandleFinalResult((EntityScope<TEntity, TKey> EntityScope, string ErrorMessage, bool ShouldWait) result2)
        {
            if (!string.IsNullOrEmpty(result2.ErrorMessage)) throw new InvalidOperationException(result2.ErrorMessage);
            return result2.EntityScope;
        }
    }

    private string BuildErrorMessage()
    {
        return $"Use {nameof(PickForUpdateAsync)} to get an update {nameof(EntityScope<TEntity, TKey>)} that can be used for update.";
    }

    private async Task<(EntityScope<TEntity, TKey> EntityScope, string ErrorMessage, bool ShouldWait)> GetForUpdateAsync(FilterDefinition<TEntity> filter, TimeSpan? timeout, string actor, CommitMode commitMode)
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

        var result = await Disk.UpdateOneAsync(matchFilter, update);
        if (result.Before == null)
        {
            //Document is missing or is already locked
            var docs = await GetAsync(filter).ToArrayAsync();

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

        Func<TEntity, Exception, Task> releaseAction;
        switch (commitMode)
        {
            case CommitMode.Update:
                releaseAction = ReleaseEntity(entityLock);
                break;
            case CommitMode.Delete:
                releaseAction = DeleteEntity(entityLock);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(commitMode), commitMode, null);
        }

        return (new EntityScope<TEntity, TKey>(result.Before, releaseAction), null, false);
    }

    private Func<TEntity, Exception, Task> DeleteEntity(Lock entityLock)
    {
        return (entity, exception) =>
        {
            try
            {
                return DeleteAsync(entity, entityLock, exception, false);
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
                            Message = $"Failed to delete entity. {e.Message}",
                            StackTrace = e.StackTrace,
                        },
                        ExpireTime = DateTime.MaxValue
                    }
                };
                _ = Disk.ReplaceOneAsync(errorEntity);
                _logger.LogError(e, e.Message);
                return Task.FromResult(false);
            }
            finally
            {
                _releaseEvent.Set();
            }
        };
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
                _ = Disk.ReplaceOneAsync(errorEntity);
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
        var result = await Disk.ReplaceOneAsync(updatedEntity, filter);

        if (result.Before == null) throw new InvalidOperationException("Cannot find entity before release."); //.AddData("context", pipelineContext).AddData("documentId", document.Id);
        if (result.Before.Lock == null) throw new InvalidOperationException("No lock information for document before release."); //.AddData("context", pipelineContext).AddData("documentId", document.Id);

        var after = await result.GetAfterAsync();
        if (after == null) throw new InvalidOperationException("After is null.");

        return after.Lock == null;
    }

    private async Task<bool> DeleteAsync(TEntity entity, Lock entityLock, Exception exception, bool externalUnlock /*bool resetUnlockCounter*/)
    {
        var lockTime = DateTime.UtcNow - entityLock.ExpireTime;
        var timeout = entityLock.ExpireTime - entityLock.LockTime;

        if (!externalUnlock && lockTime > timeout)
        {
            throw new LockExpiredException($"Entity was locked for {lockTime} instead of {timeout}.");
        }

        var after = await Disk.DeleteOneAsync(x => x.Id.Equals(entity.Id) && x.Lock != null && x.Lock.LockKey == entityLock.LockKey);
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
    protected LockableRepositoryCollectionBase(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<LockableRepositoryCollectionBase<TEntity, ObjectId>> logger = null, DatabaseContext databaseContext = null)
        : base(mongoDbServiceFactory, logger, databaseContext)
    {
    }
}