using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Tharga.MongoDB.Disk;
using Tharga.MongoDB.Internals;

namespace Tharga.MongoDB.Lockable;

public class LockableRepositoryCollectionBase<TEntity, TKey> : RepositoryCollectionBase<TEntity, TKey>, ILockableRepositoryCollection<TEntity, TKey>
    where TEntity : LockableEntityBase<TKey>
{
    private DiskRepositoryCollectionBase<TEntity, TKey> _disk;
    // ReSharper disable once StaticMemberInGenericType
    private static readonly AutoResetEvent _releaseEvent = new(false);

    protected LockableRepositoryCollectionBase(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<LockableRepositoryCollectionBase<TEntity, TKey>> logger = null, DatabaseContext databaseContext = null)
        : base(mongoDbServiceFactory, logger, databaseContext)
    {
    }

    public event EventHandler<LockEventArgs<TEntity>> LockEvent;

    internal override IRepositoryCollection<TEntity, TKey> BaseCollection => Disk;
    private DiskRepositoryCollectionBase<TEntity, TKey> Disk => _disk ??= new GenericDiskRepositoryCollection<TEntity, TKey>(_mongoDbServiceFactory, _databaseContext ?? new DatabaseContext { CollectionName = CollectionName, DatabasePart = DatabasePart, ConfigurationName = ConfigurationName }, _logger, this);
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

    //Read
    public override IAsyncEnumerable<TEntity> GetAsync(Expression<Func<TEntity, bool>> predicate = null, Options<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        return Disk.GetAsync(predicate, options, cancellationToken);
    }

    public override IAsyncEnumerable<TEntity> GetAsync(FilterDefinition<TEntity> filter, Options<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        return Disk.GetAsync(filter, options, cancellationToken);
    }

    public override IAsyncEnumerable<T> GetProjectionAsync<T>(Expression<Func<TEntity, bool>> predicate = null, Options<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        return Disk.GetProjectionAsync<T>(predicate, options, cancellationToken);
    }

    public override IAsyncEnumerable<T> GetProjectionAsync<T>(FilterDefinition<TEntity> filter, Options<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        return Disk.GetProjectionAsync<T>(filter, options, cancellationToken);
    }

    public override Task<Result<TEntity, TKey>> GetManyAsync(Expression<Func<TEntity, bool>> predicate = null, Options<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        return Disk.GetManyAsync(predicate, options, cancellationToken);
    }

    public override Task<Result<TEntity, TKey>> GetManyAsync(FilterDefinition<TEntity> filter, Options<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        return Disk.GetManyAsync(filter, options, cancellationToken);
    }

    public override Task<Result<T>> GetManyProjectionAsync<T>(Expression<Func<TEntity, bool>> predicate = null, Options<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        return Disk.GetManyProjectionAsync<T>(predicate, options, cancellationToken);
    }

    public override Task<Result<T>> GetManyProjectionAsync<T>(FilterDefinition<TEntity> filter, Options<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        return Disk.GetManyProjectionAsync<T>(filter, options, cancellationToken);
    }

    public override Task<TEntity> GetOneAsync(TKey id, CancellationToken cancellationToken = default)
    {
        return Disk.GetOneAsync(id, cancellationToken);
    }

    public override Task<TEntity> GetOneAsync(Expression<Func<TEntity, bool>> predicate, OneOption<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        return Disk.GetOneAsync(predicate, options, cancellationToken);
    }

    public override Task<TEntity> GetOneAsync(FilterDefinition<TEntity> filter, OneOption<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        return Disk.GetOneAsync(filter, options, cancellationToken);
    }

    public override Task<long> CountAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        return Disk.CountAsync(predicate, cancellationToken);
    }

    public override Task<long> CountAsync(FilterDefinition<TEntity> filter, CancellationToken cancellationToken = default)
    {
        return Disk.CountAsync(filter, cancellationToken);
    }

    public override Task<long> GetSizeAsync(CancellationToken cancellationToken = default)
    {
        return Disk.GetSizeAsync(cancellationToken);
    }

    //Create
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
        return Disk.AddManyAsync(entities);
    }

    //Update
    public Task<long> UpdateUnlockedAsync(Expression<Func<TEntity, bool>> predicate, UpdateDefinition<TEntity> update)
    {
        var filter = Builders<TEntity>.Filter.Where(predicate);
        return UpdateUnlockedAsync(filter, update);
    }

    public async Task<long> UpdateUnlockedAsync(FilterDefinition<TEntity> filter, UpdateDefinition<TEntity> update)
    {
        var filters = new FilterDefinitionBuilder<TEntity>().And(UnlockedOrExpiredFilter, filter);
        return await Disk.UpdateManyAsync(filters, update);
    }

    //Delete
    public override async Task<TEntity> DeleteOneAsync(TKey id)
    {
        var scope = await PickForDeleteAsync(id);
        var item = await scope.CommitAsync();
        return item;
    }

    public Task<TEntity> DeleteOneUnlockedAsync(Expression<Func<TEntity, bool>> predicate, OneOption<TEntity> options = null)
    {
        return Disk.DeleteOneAsync(UnlockedOrExpiredFilter.AndAlso(predicate), options);
    }

    public Task<TEntity> DeleteOneUnlockedAsync(FilterDefinition<TEntity> filter, OneOption<TEntity> options = null)
    {
        var unlockedOrExpired = Builders<TEntity>.Filter.Where(UnlockedOrExpiredFilter);

        if (filter == null) return Disk.DeleteOneAsync(unlockedOrExpired, options);

        var combined = Builders<TEntity>.Filter.And(unlockedOrExpired, filter);
        return Disk.DeleteOneAsync(combined, options);
    }

    public Task<long> DeleteManyUnlockedAsync(Expression<Func<TEntity, bool>> predicate)
    {
        return Disk.DeleteManyAsync(UnlockedOrExpiredFilter.AndAlso(predicate));
    }

    public Task<long> DeleteManyUnlockedAsync(FilterDefinition<TEntity> filter)
    {
        var unlockedOrExpired = Builders<TEntity>.Filter.Where(UnlockedOrExpiredFilter);

        if (filter == null || filter == FilterDefinition<TEntity>.Empty) return Disk.DeleteManyAsync(unlockedOrExpired);

        var combined = Builders<TEntity>.Filter.And(unlockedOrExpired, filter);
        return Disk.DeleteManyAsync(combined);
    }

    public async Task<long> DeleteManyAsync(DeleteMode deleteMode, Expression<Func<TEntity, bool>> predicate = null)
    {
        switch (deleteMode)
        {
            case DeleteMode.Exception:
                return await Disk.DeleteManyAsync(ExceptionFilter.AndAlso(predicate ?? (_ => true)));
            default:
                throw new ArgumentOutOfRangeException(nameof(deleteMode), deleteMode, null);
        }
    }

    //Other
    public override Task DropCollectionAsync()
    {
        return Disk.DropCollectionAsync();
    }

    public override IAsyncEnumerable<TEntity> GetDirtyAsync()
    {
        return Disk.GetDirtyAsync();
    }

    public override IEnumerable<(IndexFailOperation Operation, string Name)> GetFailedIndices()
    {
        return Disk.GetFailedIndices();
    }

    internal override Task<StepResponse<IMongoCollection<TEntity>>> FetchCollectionAsync(bool initiate = true)
    {
        return Disk.FetchCollectionAsync(initiate);
    }

    internal override Task<bool> AssureIndex(IMongoCollection<TEntity> collection, bool forceAssure = false, bool throwOnException = false)
    {
        return Disk.AssureIndex(collection, forceAssure, throwOnException);
    }

    internal override Task<(int Before, int After)> DropIndex(IMongoCollection<TEntity> collection)
    {
        return Disk.DropIndex(collection);
    }

    internal override Task CleanAsync(IMongoCollection<TEntity> collection)
    {
        return Disk.CleanAsync(collection);
    }

    //Lock
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

    public async IAsyncEnumerable<TEntity> GetUnlockedAsync(Expression<Func<TEntity, bool>> predicate = null, Options<TEntity> options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in Disk.GetAsync(UnlockedOrExpiredFilter.AndAlso(predicate ?? (x => true)), options, cancellationToken))
        {
            yield return item;
        }
    }

    public IAsyncEnumerable<TEntity> GetUnlockedAsync(FilterDefinition<TEntity> filter, Options<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        var baseFilter = Builders<TEntity>.Filter.Where(UnlockedOrExpiredFilter);
        var combinedFilter = Builders<TEntity>.Filter.And(baseFilter, filter);

        return Disk.GetAsync(combinedFilter, options, cancellationToken);
    }

    public async Task<EntityScope<TEntity, TKey>> PickForUpdateAsync(TKey id, TimeSpan? timeout = null, string actor = null, Func<CallbackResult<TEntity>, Task> completeAction = null)
    {
        var result = await CreateLockAsync(Builders<TEntity>.Filter.Eq(x => x.Id, id), timeout, actor, CommitMode.Update, completeAction, true);
        ThrowException(result);
        return result.EntityScope;
    }

    public async Task<EntityScope<TEntity, TKey>> PickForUpdateAsync(FilterDefinition<TEntity> filter, TimeSpan? timeout = null, string actor = null, Func<CallbackResult<TEntity>, Task> completeAction = null)
    {
        var result = await CreateLockAsync(filter, timeout, actor, CommitMode.Update, completeAction, false);
        ThrowException(result);
        return result.EntityScope;
    }

    public async Task<EntityScope<TEntity, TKey>> PickForUpdateAsync(Expression<Func<TEntity, bool>> predicate = null, TimeSpan? timeout = null, string actor = null, Func<CallbackResult<TEntity>, Task> completeAction = null)
    {
        var result = await CreateLockAsync(predicate ?? (x => true), timeout, actor, CommitMode.Update, completeAction, false);
        ThrowException(result);
        return result.EntityScope;
    }

    public async Task<EntityScope<TEntity, TKey>> PickForDeleteAsync(TKey id, TimeSpan? timeout = null, string actor = null, Func<CallbackResult<TEntity>, Task> completeAction = null)
    {
        var result = await CreateLockAsync(Builders<TEntity>.Filter.Eq(x => x.Id, id), timeout, actor, CommitMode.Delete, completeAction, true);
        ThrowException(result);
        return result.EntityScope;
    }

    public async Task<EntityScope<TEntity, TKey>> PickForDeleteAsync(FilterDefinition<TEntity> filter, TimeSpan? timeout = null, string actor = null, Func<CallbackResult<TEntity>, Task> completeAction = null)
    {
        var result = await CreateLockAsync(filter, timeout, actor, CommitMode.Delete, completeAction, false);
        ThrowException(result);
        return result.EntityScope;
    }

    public async Task<EntityScope<TEntity, TKey>> PickForDeleteAsync(Expression<Func<TEntity, bool>> predicate = null, TimeSpan? timeout = null, string actor = null, Func<CallbackResult<TEntity>, Task> completeAction = null)
    {
        var result = await CreateLockAsync(predicate ?? (x => true), timeout, actor, CommitMode.Delete, completeAction, false);
        ThrowException(result);
        return result.EntityScope;
    }

    public async Task<EntityScope<TEntity, TKey>> WaitForUpdateAsync(TKey id, TimeSpan? lockTimeout = null, TimeSpan? waitTimeout = null, string actor = null, Func<CallbackResult<TEntity>, Task> completeAction = default, CancellationToken cancellationToken = default)
    {
        return await WaitForLock(id, lockTimeout, waitTimeout, actor, cancellationToken, CommitMode.Update, completeAction);
    }

    public async Task<EntityScope<TEntity, TKey>> WaitForDeleteAsync(TKey id, TimeSpan? lockTimeout = null, TimeSpan? waitTimeout = null, string actor = null, Func<CallbackResult<TEntity>, Task> completeAction = null, CancellationToken cancellationToken = default)
    {
        return await WaitForLock(id, lockTimeout, waitTimeout, actor, cancellationToken, CommitMode.Delete, completeAction);
    }

    public IAsyncEnumerable<EntityLock<TEntity, TKey>> GetWithLockInfoAsync(FilterDefinition<TEntity> filter = null, Options<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        return Disk.GetAsync(filter, options, cancellationToken)
            .Select(x => new EntityLock<TEntity, TKey>(x, x.Lock));
    }

    public IAsyncEnumerable<EntityLock<TEntity, TKey>> GetLockedAsync(LockMode lockMode, FilterDefinition<TEntity> filter = null, Options<TEntity> options = null, CancellationToken cancellationToken = default)
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

    public IAsyncEnumerable<EntityLock<TEntity, TKey>> GetExpiredAsync(FilterDefinition<TEntity> filter = null, Options<TEntity> options = null, CancellationToken cancellationToken = default)
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

    public async Task<EntityChangeResult<TEntity>> ReleaseOneAsync(TKey id, ReleaseMode mode)
    {
        var filter = Builders<TEntity>.Filter.And(Builders<TEntity>.Filter.Eq(x => x.Id, id), BuildReleaseFilter(mode));
        var update = new UpdateDefinitionBuilder<TEntity>().Set(x => x.Lock, null);
        var result = await Disk.UpdateOneAsync(filter, update, OneOption<TEntity>.FirstOrDefault); //Use FirstOrDefault since it is atomic safe.
        return result;
    }

    public async Task<long> ReleaseManyAsync(ReleaseMode mode)
    {
        var filter = BuildReleaseFilter(mode);
        var update = new UpdateDefinitionBuilder<TEntity>().Set(x => x.Lock, null);
        var result = await Disk.UpdateManyAsync(filter, update);
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
            var result = await CreateLockAsync(Builders<TEntity>.Filter.Eq(x => x.Id, id), lockTimeout ?? DefaultTimeout, actor, commitMode, completeAction, true);
            if (!result.ShouldWait) return HandleFinalResult(result);
            WaitHandle.WaitAny(waitHandles, recheckTimeInterval);
        }

        if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException("The operation was canceled.");

        var finalCheckResult = await CreateLockAsync(Builders<TEntity>.Filter.Eq(x => x.Id, id), lockTimeout ?? DefaultTimeout, actor, commitMode, completeAction, true);
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

    private async Task<(EntityScope<TEntity, TKey> EntityScope, ErrorInfo errorInfo, bool ShouldWait)> CreateLockAsync(FilterDefinition<TEntity> filter, TimeSpan? timeout, string actor, CommitMode commitMode, Func<CallbackResult<TEntity>, Task> completeAction, bool failIfLocked)
    {
        var timeoutTotUse = timeout ?? DefaultTimeout;
        if (timeoutTotUse.Ticks < 0) throw new ArgumentException($"{nameof(timeout)} cannot be less than zero. Provided or default value is {timeoutTotUse}.");
        if (RequireActor && actor.IsNullOrEmpty()) throw new ArgumentNullException(nameof(actor), $"No {nameof(actor)} provided. Set {nameof(RequireActor)} to false or provide {nameof(actor)}.");

        var now = DateTime.UtcNow;
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
            Builders<TEntity>.Filter.Lt(x => x.Lock.ExpireTime, now)
        );
        var matchFilter = Builders<TEntity>.Filter.Or(unlockedFilter, expiredLockFilter);

        var entityLock = new Lock
        {
            LockKey = lockKey,
            LockTime = now,
            ExpireTime = now.Add(timeoutTotUse),
            Actor = actor,
            ExceptionInfo = null,
        };

        var update = new UpdateDefinitionBuilder<TEntity>().Set(x => x.Lock, entityLock);

        var result = await Disk.UpdateOneAsync(matchFilter, update, OneOption<TEntity>.FirstOrDefault); //Use FirstOrDefault since it is atomic safe.
        if (result.Before == null) //Document is missing or is already locked
        {
            if (!failIfLocked) return (null, null, false); //No document matches the filter.

            var docs = await GetAsync(filter).ToArrayAsync();

            if (!docs.Any()) return (null, null, false); //No document matches the filter.

            if (docs.Length == 1)
            {
                var doc = docs.Single();
                if (doc.Lock?.ExceptionInfo == null)
                {
                    var timeString = doc.Lock == null ? null : $" for {doc.Lock.ExpireTime - now}";
                    var actorString = doc.Lock?.Actor == null ? null : $" by '{doc.Lock.Actor}'";

                    return (null, new ErrorInfo
                    {
                        Message = $"Entity with id '{doc.Id}' is locked{actorString}{timeString}.",
                        Type = ErrorInfoType.Locked
                    }, true);
                }

                if (doc.Lock.ExceptionInfo != null)
                {
                    return (null, new ErrorInfo
                    {
                        Message = $"Entity with id '{doc.Id}' has an exception attached.",
                        Type = ErrorInfoType.Error
                    }, false);
                }

                return (null, new ErrorInfo
                {
                    Message = $"Entity with id '{doc.Id}' has an unknown state.",
                    Type = ErrorInfoType.Unknown
                }, false);
            }

            return (null, new ErrorInfo
            {
                Message = $"Matched with {docs.Length} documents but no unlocked",
                Type = ErrorInfoType.Unknown
            }, false);
        }
        else
        {
            FireAndForgetEvent(() => Task.FromResult(result.Before), LockAction.Locked); //NOTE: using before here would be same as after, since we are not getting the lock information.
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

    private async Task<bool> ReleaseAsync(TEntity entity, Lock entityLock, bool commit, Exception exception, Func<CallbackResult<TEntity>, Task> completeAction, Func<TEntity, Lock, Func<CallbackResult<TEntity>, Task>, Lock, Task<(EntityChangeResult<TEntity>, LockAction)>> releaseAction)
    {
        if (commit && exception != null) throw new ArgumentException("Cannot commit entity when there is an exception.");

        var lockTime = DateTime.UtcNow - entityLock.ExpireTime;
        var timeout = entityLock.ExpireTime - entityLock.LockTime;
        var lockInfo = BuildLockInfo(entityLock, exception);
        var expired = lockTime > timeout;

        if ((commit || exception != null) && expired)
        {
            throw new LockExpiredException($"Entity of type {typeof(TEntity).Name} was locked for {lockTime} instead of {timeout}.");
        }

        EntityChangeResult<TEntity> result;
        TEntity after = null;
        LockAction lockAction;
        if (commit)
        {
            var r = await releaseAction.Invoke(entity, entityLock, completeAction, lockInfo);
            result = r.Item1;
            lockAction = r.Item2;
            if (result == null) return true;
        }
        else
        {
            var update = new UpdateDefinitionBuilder<TEntity>()
                .Set(x => x.Lock, lockInfo);
            result = await Disk.UpdateOneAsync(entity.Id, update);

            lockAction = lockInfo?.ExceptionInfo != null ? LockAction.Exception : LockAction.Abandoned;

            if (LockEvent != null)
            {
                after = await result.GetAfterAsync();
                var afterCopy = after;
                FireAndForgetEvent(() => Task.FromResult(afterCopy), lockAction);
            }
        }

        if (result.Before == null) throw new InvalidOperationException("Cannot find entity before release.");
        if (result.Before.Lock == null) throw new InvalidOperationException("No lock information for document before release.");

        after ??= await result.GetAfterAsync();
        if (after == null && Debugger.IsAttached) throw new InvalidOperationException($"Entity {typeof(TEntity).Name} with id '{entity.Id}' does not exist after release.");

        if (completeAction != null && !expired)
        {
            await completeAction.Invoke(new CallbackResult<TEntity> { LockAction = lockAction, Before = result.Before, After = after });
        }

        return after?.Lock == null;
    }

    private async Task<(EntityChangeResult<TEntity>, LockAction)> PrepareCommitForUpdateAsync(TEntity entity, Lock entityLock, Func<CallbackResult<TEntity>, Task> completeAction, Lock lockInfo)
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
        var result = await Disk.ReplaceOneAsync(updatedEntity, filter, OneOption<TEntity>.FirstOrDefault); //Use FirstOrDefault since it is atomic safe.

        FireAndForgetEvent(async () => await result.GetAfterAsync(), LockAction.CommitUpdated);

        return (result, LockAction.CommitUpdated);
    }

    private async Task<(EntityChangeResult<TEntity>, LockAction)> PerformCommitForDeleteAsync(TEntity entity, Lock entityLock, Func<CallbackResult<TEntity>, Task> completeAction, Lock lockInfo)
    {
        var before = await Disk.DeleteOneAsync(x => x.Id.Equals(entity.Id) && x.Lock != null && x.Lock.LockKey == entityLock.LockKey);
        if (before == null)
        {
            var item = await Disk.GetOneAsync(entity.Id);
            if (item == null) throw new InvalidOperationException($"Entity of type {typeof(TEntity).Name} with id '{entity.Id}' was not deleted on commit, it did not exist. [EntityActor: {entity.Lock?.Actor}, EntityLockTime: {entity.Lock?.LockTime}, EntityExpireTime: {entity.Lock?.ExpireTime}, EntityException: {entity.Lock?.ExceptionInfo?.Message}, Now: {DateTime.UtcNow}]");
            throw new InvalidOperationException($"Entity of type {typeof(TEntity).Name} with id '{entity.Id}' was not deleted on commit, lock key missmatch. [EntityActor: {entity.Lock?.Actor}, EntityLockTime: {entity.Lock?.LockTime}, EntityExpireTime: {entity.Lock?.ExpireTime}, EntityException: {entity.Lock?.ExceptionInfo?.Message}, CurrentActor: {item.Lock?.Actor}, CurrentLockTime: {item.Lock?.LockTime}, CurrentExpireTime: {item.Lock?.ExpireTime}, CurrentException: {item.Lock?.ExceptionInfo?.Message}, Now: {DateTime.UtcNow}]");
        }

        FireAndForgetEvent(() => Task.FromResult(before), LockAction.CommitDeleted);

        if (completeAction != null)
        {
            await completeAction.Invoke(new CallbackResult<TEntity> { LockAction = LockAction.CommitDeleted, Before = before, After = null });
        }

        return (null, LockAction.CommitDeleted);
    }

    private void FireAndForgetEvent(Func<Task<TEntity>> entityLoader, LockAction lockAction)
    {
        if (LockEvent == null) return;

        Task.Run(async () =>
        {
            try
            {
                var entity = await entityLoader.Invoke();
                LockEvent?.Invoke(this, new LockEventArgs<TEntity>(entity, lockAction));
            }
            catch (Exception e)
            {
                _logger?.LogWarning(e, e.Message);
            }
        });
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