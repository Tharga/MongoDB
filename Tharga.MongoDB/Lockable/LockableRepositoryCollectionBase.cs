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
using Tharga.Toolkit;

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
    internal override IEnumerable<CreateIndexModel<TEntity>> CoreIndices =>
    [
        new(Builders<TEntity>.IndexKeys.Ascending(x => x.Lock), new CreateIndexOptions { Name = nameof(LockableEntityBase.Lock) }),
        new(
            Builders<TEntity>.IndexKeys
                .Ascending(x => x.Lock.ExceptionInfo)
                .Ascending(x => x.Lock.ExpireTime)
                .Ascending(x => x.Lock.LockTime),
            new CreateIndexOptions { Name = "LockStatus" }
        )
    ];

    protected virtual TimeSpan DefaultTimeout { get; init; } = TimeSpan.FromSeconds(30);
    protected virtual bool RequireActor => true;

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

    public Expression<Func<TEntity, bool>> UnlockedOrExpiredFilter
    {
        get
        {
            var now = DateTime.UtcNow;
            Expression<Func<TEntity, bool>> expression = x =>
                x.Lock == null
                || (x.Lock.ExceptionInfo == null && x.Lock.ExpireTime < now);

            return expression;
        }
    }

    public Expression<Func<TEntity, bool>> LockedOrExceptionFilter
    {
        get
        {
            var now = DateTime.UtcNow;
            Expression<Func<TEntity, bool>> expression = x =>
                x.Lock != null
                && (x.Lock.ExceptionInfo != null || x.Lock.ExpireTime >= now);

            return expression;
        }
    }

    public Expression<Func<TEntity, bool>> ExceptionFilter
    {
        get
        {
            Expression<Func<TEntity, bool>> expression = x =>
                x.Lock != null
                && x.Lock.ExceptionInfo != null;

            return expression;
        }
    }

    public Expression<Func<TEntity, bool>> LockedFilter
    {
        get
        {
            var now = DateTime.UtcNow;
            Expression<Func<TEntity, bool>> expression = x =>
                x.Lock != null
                && x.Lock.ExpireTime >= now;

            return expression;
        }
    }

    public async IAsyncEnumerable<TEntity> GetUnlockedAsync(Expression<Func<TEntity, bool>> predicate = null, Options<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        await foreach (var page in Disk.GetPagesAsync(UnlockedOrExpiredFilter.AndAlso(predicate ?? (x => true)), options, cancellationToken))
        {
            await foreach (var item in page.Items.WithCancellation(cancellationToken))
            {
                yield return item;
            }
        }
    }

    public IAsyncEnumerable<TEntity> GetUnlockedAsync(FilterDefinition<TEntity> filter, Options<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        var baseFilter = Builders<TEntity>.Filter.Where(UnlockedOrExpiredFilter);
        var combinedFilter = Builders<TEntity>.Filter.And(baseFilter, filter);

        return Disk.GetAsync(combinedFilter, options, cancellationToken);
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

    public override async Task<long> UpdateAsync(FilterDefinition<TEntity> filter, UpdateDefinition<TEntity> update)
    {
        var filters = new FilterDefinitionBuilder<TEntity>().And(UnlockedOrExpiredFilter, filter);
        return await Disk.UpdateAsync(filters, update);
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

    public override async Task<TEntity> DeleteOneAsync(TKey id)
    {
        var scope = await PickForDeleteAsync(id);
        var item = await scope.CommitAsync();
        return item;
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
        return await Disk.DeleteManyAsync(UnlockedOrExpiredFilter.AndAlso(predicate));
    }

    public async Task<long> DeleteManyAsync(DeleteMode deleteMode, Expression<Func<TEntity, bool>> predicate = default)
    {
        switch (deleteMode)
        {
            case DeleteMode.Exception:
                return await Disk.DeleteManyAsync(ExceptionFilter.AndAlso(predicate ?? (_ => true)));
            default:
                throw new ArgumentOutOfRangeException(nameof(deleteMode), deleteMode, null);
        }
    }

    public override IMongoCollection<TEntity> GetCollection()
    {
        return Disk.GetCollection();
    }

    public override Task DropCollectionAsync()
    {
        return Disk.DropCollectionAsync();
    }

    public async Task<EntityScope<TEntity, TKey>> PickForUpdateAsync(TKey id, TimeSpan? timeout = default, string actor = default, Func<CallbackResult<TEntity>, Task> completeAction = default)
    {
        var result = await CreateLockAsync(Builders<TEntity>.Filter.Eq(x => x.Id, id), timeout, actor, CommitMode.Update, completeAction);
        ThrowException(result);
        return result.EntityScope;
    }

    public async Task<EntityScope<TEntity, TKey>> PickForDeleteAsync(TKey id, TimeSpan? timeout = default, string actor = default, Func<CallbackResult<TEntity>, Task> completeAction = default)
    {
        var result = await CreateLockAsync(Builders<TEntity>.Filter.Eq(x => x.Id, id), timeout, actor, CommitMode.Delete, completeAction);
        ThrowException(result);
        return result.EntityScope;
    }

    public async Task<EntityScope<TEntity, TKey>> WaitForUpdateAsync(TKey id, TimeSpan? lockTimeout = default, TimeSpan? waitTimeout = default, string actor = default, Func<CallbackResult<TEntity>, Task> completeAction = default, CancellationToken cancellationToken = default)
    {
        return await WaitForLock(id, lockTimeout, waitTimeout, actor, cancellationToken, CommitMode.Update, completeAction);
    }

    public async Task<EntityScope<TEntity, TKey>> WaitForDeleteAsync(TKey id, TimeSpan? lockTimeout = default, TimeSpan? waitTimeout = default, string actor = default, Func<CallbackResult<TEntity>, Task> completeAction = default, CancellationToken cancellationToken = default)
    {
        return await WaitForLock(id, lockTimeout, waitTimeout, actor, cancellationToken, CommitMode.Delete, completeAction);
    }

    public IAsyncEnumerable<EntityLock<TEntity, TKey>> GetLockedAsync(LockMode lockMode, FilterDefinition<TEntity> filter = default, Options<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var builder = Builders<TEntity>.Filter;
        var filters = new List<FilterDefinition<TEntity>>
        {
            new FilterDefinitionBuilder<TEntity>().Ne(x => x.Lock, null)
        };
        if (filter != null) filters.Add(filter);

        if (lockMode.HasFlag(LockMode.Locked) && lockMode.HasFlag(LockMode.Exception))
        {
            filters.Add(
                Builders<TEntity>.Filter.Or(
                    new FilterDefinitionBuilder<TEntity>().Ne(x => x.Lock.ExceptionInfo, null),
                    Builders<TEntity>.Filter.And(
                        new FilterDefinitionBuilder<TEntity>().Eq(x => x.Lock.ExceptionInfo, null),
                        new FilterDefinitionBuilder<TEntity>().Gte(x => x.Lock.ExpireTime, now)
                    )
                )
            );
        }
        else if (lockMode.HasFlag(LockMode.Locked))
        {
            filters.Add(new FilterDefinitionBuilder<TEntity>().Eq(x => x.Lock.ExceptionInfo, null));
            filters.Add(new FilterDefinitionBuilder<TEntity>().Gte(x => x.Lock.ExpireTime, now));
        }
        else if (lockMode.HasFlag(LockMode.Exception))
        {
            filters.Add(new FilterDefinitionBuilder<TEntity>().Ne(x => x.Lock.ExceptionInfo, null));
        }

        return Disk.GetAsync(builder.And(filters), options, cancellationToken)
            .Select(x => new EntityLock<TEntity, TKey>(x, x.Lock));
    }

    public IAsyncEnumerable<EntityLock<TEntity, TKey>> GetExpiredAsync(FilterDefinition<TEntity> filter = default, Options<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var builder = Builders<TEntity>.Filter;
        var filters = new List<FilterDefinition<TEntity>>
        {
            new FilterDefinitionBuilder<TEntity>().Ne(x => x.Lock, null),
            new FilterDefinitionBuilder<TEntity>().Eq(x => x.Lock.ExceptionInfo, null),
            new FilterDefinitionBuilder<TEntity>().Lt(x => x.Lock.ExpireTime, now)
        };

        if (filter != null) filters.Add(filter);

        return Disk.GetAsync(builder.And(filters), options, cancellationToken)
            .Select(x => new EntityLock<TEntity, TKey>(x, x.Lock));
    }

    /// <summary>
    /// Release locked document depending on mode.
    /// </summary>
    /// <param name="id"></param>
    /// <param name="mode"></param>
    /// <returns></returns>
    public async Task<EntityChangeResult<TEntity>> ReleaseOneAsync(TKey id, ReleaseMode mode)
    {
        var filter = Builders<TEntity>.Filter.And(Builders<TEntity>.Filter.Eq(x => x.Id, id), BuildReleaseFilter(mode));
        var update = new UpdateDefinitionBuilder<TEntity>().Set(x => x.Lock, null);
        var result = await Disk.UpdateOneAsync(filter, update);
        return result;
    }

    public async Task<long> ReleaseManyAsync(ReleaseMode mode)
    {
        var filter = BuildReleaseFilter(mode);
        var update = new UpdateDefinitionBuilder<TEntity>().Set(x => x.Lock, null);
        var result = await Disk.UpdateAsync(filter, update);
        return result;
    }

    private static FilterDefinition<TEntity> BuildReleaseFilter(ReleaseMode mode)
    {
        FilterDefinition<TEntity> filter;
        switch (mode)
        {
            case ReleaseMode.ExceptionOnly:
                filter = Builders<TEntity>.Filter.And(
                    Builders<TEntity>.Filter.Ne(x => x.Lock, null),
                    Builders<TEntity>.Filter.Ne(x => x.Lock.ExceptionInfo, null)
                );
                break;
            case ReleaseMode.LockOnly:
                filter = Builders<TEntity>.Filter.And(
                    Builders<TEntity>.Filter.Ne(x => x.Lock, null),
                    Builders<TEntity>.Filter.Eq(x => x.Lock.ExceptionInfo, null)
                );
                break;
            case ReleaseMode.Any:
                filter = Builders<TEntity>.Filter.And(
                    Builders<TEntity>.Filter.Ne(x => x.Lock, null)
                );
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
        }

        return filter;
    }

    private async Task<EntityScope<TEntity, TKey>> WaitForLock(TKey id, TimeSpan? lockTimeout, TimeSpan? waitTimeout, string actor, CancellationToken cancellationToken, CommitMode commitMode, Func<CallbackResult<TEntity>, Task> completeAction)
    {
        var actualTimeout = lockTimeout ?? waitTimeout ?? DefaultTimeout;
        var recheckTimeInterval = actualTimeout / 5;
        using var timeoutCts = new CancellationTokenSource(actualTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        var waitHandles = new[] { _releaseEvent, linkedCts.Token.WaitHandle };

        while (!linkedCts.Token.IsCancellationRequested)
        {
            var result = await CreateLockAsync(Builders<TEntity>.Filter.Eq(x => x.Id, id), lockTimeout ?? DefaultTimeout, actor, commitMode, completeAction);
            if (!result.ShouldWait) return HandleFinalResult(result);
            WaitHandle.WaitAny(waitHandles, recheckTimeInterval);
        }

        if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException("The operation was canceled.");

        var finalCheckResult = await CreateLockAsync(Builders<TEntity>.Filter.Eq(x => x.Id, id), lockTimeout ?? DefaultTimeout, actor, commitMode, completeAction);
        if (!finalCheckResult.ShouldWait)
        {
            return HandleFinalResult(finalCheckResult);
        }

        throw new TimeoutException("No valid entity has been released for update.");

        EntityScope<TEntity, TKey> HandleFinalResult((EntityScope<TEntity, TKey> EntityScope, ErrorInfo errorInfo, bool ShouldWait) result)
        {
            ThrowException(result);
            return result.EntityScope;
        }
    }

    private string BuildErrorMessage()
    {
        return $"Use {nameof(PickForUpdateAsync)} to get an update {nameof(EntityScope<TEntity, TKey>)} that can be used for update.";
    }

    private async Task<(EntityScope<TEntity, TKey> EntityScope, ErrorInfo errorInfo, bool ShouldWait)> CreateLockAsync(FilterDefinition<TEntity> filter, TimeSpan? timeout, string actor, CommitMode commitMode, Func<CallbackResult<TEntity>, Task> completeAction)
    {
        var timeoutTotUse = timeout ?? DefaultTimeout;
        if (timeoutTotUse.Ticks < 0) throw new ArgumentException($"{nameof(timeout)} cannot be less than zero. Provided or default value is {timeoutTotUse}.");
        if (RequireActor && actor.IsNullOrEmpty()) throw new ArgumentNullException(nameof(actor), $"No {nameof(actor)} provided.");

        var now = DateTime.UtcNow;
        var lockKey = Guid.NewGuid();
        actor = StringExtensions.NullIfEmpty(actor);

        var unlockedFilter = Builders<TEntity>.Filter.And(
            filter,
            Builders<TEntity>.Filter.Eq(x => x.Lock, null)
        );
        var expiredLockFilter = Builders<TEntity>.Filter.And(
            filter,
            Builders<TEntity>.Filter.Ne(x => x.Lock, null),
            Builders<TEntity>.Filter.Eq(x => x.Lock.ExceptionInfo, null),
            Builders<TEntity>.Filter.Lt(x => x.Lock.ExpireTime, now)
        );
        var matchFilter = Builders<TEntity>.Filter.Or(unlockedFilter, expiredLockFilter);

        var entityLock = new Lock
        {
            LockKey = lockKey,
            LockTime = now,
            ExpireTime = now.Add(timeoutTotUse),
            Actor = actor,
            ExceptionInfo = default,
        };

        var update = new UpdateDefinitionBuilder<TEntity>().Set(x => x.Lock, entityLock);

        var result = await Disk.UpdateOneAsync(matchFilter, update);
        if (result.Before == null) //Document is missing or is already locked
        {
            var docs = await GetAsync(filter).ToArrayAsync();

            if (!docs.Any()) return (default, null, false); //No document matches the filter.

            if (docs.Length == 1)
            {
                var doc = docs.Single();
                if (doc.Lock?.ExceptionInfo == null)
                {
                    var timeString = doc.Lock == null ? null : $" for {doc.Lock.ExpireTime - now}";
                    var actorString = doc.Lock?.Actor == null ? null : $" by '{doc.Lock.Actor}'";

                    return (default, new ErrorInfo
                    {
                        Message = $"Entity with id '{doc.Id}' is locked{actorString}{timeString}.",
                        Type = ErrorInfoType.Locked
                    }, true);
                }

                if (doc.Lock.ExceptionInfo != null)
                {
                    return (default, new ErrorInfo
                    {
                        Message = $"Entity with id '{doc.Id}' has an exception attached.",
                        Type = ErrorInfoType.Error
                    }, false);
                }

                return (default, new ErrorInfo
                {
                    Message = $"Entity with id '{doc.Id}' has an unknown state.",
                    Type = ErrorInfoType.Unknown
                }, false);
            }

            throw new NotSupportedException("Multiple documents matches with the provided expression.");
        }

        if (result.Before.Lock != null)
        {
            if (now <= result.Before.Lock.ExpireTime)
            {
                throw new NotSupportedException($"The entity that was picked had a lock that has not yet expired. [ExpireTime: {result.Before.Lock.ExpireTime}, LockTime: {result.Before.Lock.LockTime}, BeforeActor: {result.Before.Lock.Actor}, Actor: {actor}, Now: {now}]");
            }

            if (result.Before.Lock.ExceptionInfo != null)
            {
                throw new NotSupportedException($"The entity that was picked had an exception attached. [ExpireTime: {result.Before.Lock.ExpireTime}, LockTime: {result.Before.Lock.LockTime}, Exception: {result.Before.Lock.ExceptionInfo.Message}, BeforeActor: {result.Before.Lock.Actor}, Actor: {actor}, Now: {now}]");
            }

            _logger?.LogInformation("{Actor} picked an entity from {BeforeActor} that expired {Expired} ago.", actor, result.Before.Lock.Actor, result.Before.Lock.ExpireTime - now);
        }

        Func<TEntity, bool, Exception, Task> releaseAction;
        try
        {
            switch (commitMode)
            {
                case CommitMode.Update:
                    releaseAction = (entity, commit, exception) => ReleaseAsync(entity, entityLock, commit, exception, completeAction, PrepareCommitForUpdateAsync);
                    break;
                case CommitMode.Delete:
                    releaseAction = (entity, commit, exception) => ReleaseAsync(entity, entityLock, commit, exception, completeAction, PerformCommitForDeleteAsync);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(commitMode), commitMode, null);
            }
        }
        finally
        {
            _releaseEvent.Set();
        }

        return (new EntityScope<TEntity, TKey>(result.Before, releaseAction), null, false);
    }

    private async Task<bool> ReleaseAsync(TEntity entity, Lock entityLock, bool commit, Exception exception, Func<CallbackResult<TEntity>, Task> completeAction, Func<TEntity, Lock, Func<CallbackResult<TEntity>, Task>, Lock, Task<EntityChangeResult<TEntity>>> releaseAction)
    {
        var lockTime = DateTime.UtcNow - entityLock.ExpireTime;
        var timeout = entityLock.ExpireTime - entityLock.LockTime;
        var lockInfo = BuildLockInfo(entityLock, exception);
        var expired = lockTime > timeout;

        if ((commit || exception != null) && expired)
        {
            throw new LockExpiredException($"Entity of type {typeof(TEntity).Name} was locked for {lockTime} instead of {timeout}.");
        }

        EntityChangeResult<TEntity> result;
        if (commit)
        {
            result = await releaseAction.Invoke(entity, entityLock, completeAction, lockInfo);
            if (result == default) return true;
        }
        else
        {
            var update = new UpdateDefinitionBuilder<TEntity>()
                .Set(x => x.Lock, lockInfo);
            result = await Disk.UpdateOneAsync(entity.Id, update);
        }

        if (result.Before == null) throw new InvalidOperationException("Cannot find entity before release.");
        if (result.Before.Lock == null) throw new InvalidOperationException("No lock information for document before release.");

        var after = await result.GetAfterAsync();
        if (after == null && Debugger.IsAttached) throw new InvalidOperationException($"Entity {typeof(TEntity).Name} with id '{entity.Id}' does not exist after release.");

        if (completeAction != null && !expired)
        {
            await completeAction.Invoke(new CallbackResult<TEntity> { Commit = commit, Before = result.Before, After = after });
        }

        return after?.Lock == null;
    }

    private async Task<EntityChangeResult<TEntity>> PrepareCommitForUpdateAsync(TEntity entity, Lock entityLock, Func<CallbackResult<TEntity>, Task> completeAction, Lock lockInfo)
    {
        var updatedEntity = entity with
        {
            Lock = lockInfo
        };

        //NOTE: This filter assures that the correct lock still exists on the entity.
        var filter = Builders<TEntity>.Filter.And(
            Builders<TEntity>.Filter.Eq(x => x.Id, entity.Id),
            Builders<TEntity>.Filter.Ne(x => x.Lock, null),
            Builders<TEntity>.Filter.Eq(x => x.Lock.LockKey, entityLock.LockKey)
        );
        var result = await Disk.ReplaceOneAsync(updatedEntity, filter);
        return result;
    }

    private async Task<EntityChangeResult<TEntity>> PerformCommitForDeleteAsync(TEntity entity, Lock entityLock, Func<CallbackResult<TEntity>, Task> completeAction, Lock lockInfo)
    {
        var before = await Disk.DeleteOneAsync(x => x.Id.Equals(entity.Id) && x.Lock != null && x.Lock.LockKey == entityLock.LockKey);
        if (before == null)
        {
            var item = await Disk.GetOneAsync(x => x.Id.Equals(entity.Id));
            if (item == null) throw new InvalidOperationException($"Entity of type {typeof(TEntity).Name} with id '{entity.Id}' was not deleted on commit, it did not exist. [EntityActor: {entity.Lock?.Actor}, EntityLockTime: {entity.Lock?.LockTime}, EntityExpireTime: {entity.Lock?.ExpireTime}, EntityException: {entity.Lock?.ExceptionInfo?.Message}, Now: {DateTime.UtcNow}]");
            throw new InvalidOperationException($"Entity of type {typeof(TEntity).Name} with id '{entity.Id}' was not deleted on commit, lock key missmatch. [EntityActor: {entity.Lock?.Actor}, EntityLockTime: {entity.Lock?.LockTime}, EntityExpireTime: {entity.Lock?.ExpireTime}, EntityException: {entity.Lock?.ExceptionInfo?.Message}, CurrentActor: {item.Lock?.Actor}, CurrentLockTime: {item.Lock?.LockTime}, CurrentExpireTime: {item.Lock?.ExpireTime}, CurrentException: {item.Lock?.ExceptionInfo?.Message}, Now: {DateTime.UtcNow}]");
        }

        if (completeAction != null)
        {
            await completeAction.Invoke(new CallbackResult<TEntity> { Commit = true, Before = before, After = null });
        }

        return default;
    }

    private Lock BuildLockInfo(Lock entityLock, Exception exception)
    {
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
            });
        }

        return null;
    }

    private static void ThrowException((EntityScope<TEntity, TKey> EntityScope, ErrorInfo errorInfo, bool ShouldWait) result)
    {
        if (result.errorInfo != null)
        {
            switch (result.errorInfo.Type)
            {
                case ErrorInfoType.Locked:
                    throw new LockException(result.errorInfo.Message);
                case ErrorInfoType.Error:
                    throw new LockErrorException(result.errorInfo.Message);
                case ErrorInfoType.Unknown:
                    throw new UnknownException(result.errorInfo.Message);
                default:
                    throw new ArgumentOutOfRangeException(nameof(result.errorInfo.Type), $"Unknown type {nameof(result.errorInfo.Type)} {result.errorInfo.Type}");
            }
        }
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