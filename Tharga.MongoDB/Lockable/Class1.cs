using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Tharga.MongoDB.Disk;

namespace Tharga.MongoDB.Lockable;

public abstract record LockableEntityBase<TKey> : EntityBase<TKey>
{
    [BsonIgnoreIfNull]
    public DocumentLock DocumentLock { get; internal init; }

    [BsonIgnoreIfDefault]
    public int UnlockCounter { get; internal init; }
}

public record DocumentLock
{
    public Guid LockKey { get; internal init; }
    public DateTime LockTime { get; internal init; }

    [BsonIgnoreIfDefault]
    public DateTime? ExpireTime { get; internal init; }

    [BsonIgnoreIfDefault]
    public string Actor { get; internal init; }

    [BsonIgnoreIfDefault]
    public Exception Exception { get; internal init; }
}

public interface ILockableRepositoryCollection<TEntity, TKey> : IReadOnlyRepositoryCollection<TEntity, TKey>
    where TEntity : LockableEntityBase<TKey>
{
}

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
        //TODO: Do not for locked entities
        throw new NotImplementedException();
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
}