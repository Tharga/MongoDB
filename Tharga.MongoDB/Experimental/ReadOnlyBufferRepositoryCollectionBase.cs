using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Tharga.MongoDB.Experimental;

public abstract class RepositoryCollectionBase<TEntity, TKey>
    where TEntity : EntityBase<TKey>
{
    protected RepositoryCollectionBase(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<RepositoryCollectionBase<TEntity, TKey>> logger)
    {
    }

    public Task<long> GetSizeAsync()
    {
        throw new NotImplementedException();
    }

    public IAsyncEnumerable<TEntity> GetAsync(Expression<Func<TEntity, bool>> predicate = null, Options<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public IAsyncEnumerable<T> GetAsync<T>(Expression<Func<T, bool>> predicate = null, Options<T> options = null, CancellationToken cancellationToken = default) where T : TEntity
    {
        throw new NotImplementedException();
    }

    public Task<TEntity> GetOneAsync(TKey id, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<TEntity> GetOneAsync(Expression<Func<TEntity, bool>> predicate = null, OneOption<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<T> GetOneAsync<T>(Expression<Func<T, bool>> predicate = null, OneOption<T> options = null, CancellationToken cancellationToken = default) where T : TEntity
    {
        throw new NotImplementedException();
    }

    public Task<long> CountAsync(Expression<Func<TEntity, bool>> predicate)
    {
        throw new NotImplementedException();
    }
}

public abstract class ReadOnlyBufferRepositoryCollectionBase<TEntity, TKey> : RepositoryCollectionBase<TEntity, TKey>, IReadOnlyRepositoryCollection<TEntity, TKey>
    where TEntity : EntityBase<TKey>
{
    protected ReadOnlyBufferRepositoryCollectionBase(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<ReadOnlyBufferRepositoryCollectionBase<TEntity, TKey>> logger)
        : base(mongoDbServiceFactory, logger)
    {
    }
}

public abstract class ReadWriteBufferRepositoryCollectionBase<TEntity, TKey> : ReadOnlyBufferRepositoryCollectionBase<TEntity, TKey>, IRepositoryCollection<TEntity, TKey>
    where TEntity : EntityBase<TKey>
{
    protected ReadWriteBufferRepositoryCollectionBase(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<ReadWriteBufferRepositoryCollectionBase<TEntity, TKey>> logger)
        : base(mongoDbServiceFactory, logger)
    {
    }

    public Task DropCollectionAsync()
    {
        throw new NotImplementedException();
    }

    public Task<bool> AddAsync(TEntity entity)
    {
        throw new NotImplementedException();
    }

    public Task AddManyAsync(IEnumerable<TEntity> entities)
    {
        throw new NotImplementedException();
    }

    public Task<EntityChangeResult<TEntity>> AddOrReplaceAsync(TEntity entity)
    {
        throw new NotImplementedException();
    }

    public Task<EntityChangeResult<TEntity>> UpdateOneAsync(TKey id, UpdateDefinition<TEntity> update)
    {
        throw new NotImplementedException();
    }

    public Task<TEntity> DeleteOneAsync(TKey id)
    {
        throw new NotImplementedException();
    }

    public Task<TEntity> DeleteOneAsync(Expression<Func<TEntity, bool>> predicate = null, FindOneAndDeleteOptions<TEntity, TEntity> options = default)
    {
        throw new NotImplementedException();
    }

    public Task<long> DeleteManyAsync(Expression<Func<TEntity, bool>> predicate)
    {
        throw new NotImplementedException();
    }
}

public abstract class ReadOnlyDiskRepositoryCollectionBase<TEntity, TKey> : RepositoryCollectionBase<TEntity, TKey>, IReadOnlyDiskRepositoryCollection<TEntity, TKey>
    where TEntity : EntityBase<TKey>
{
    protected ReadOnlyDiskRepositoryCollectionBase(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<ReadOnlyDiskRepositoryCollectionBase<TEntity, TKey>> logger)
        :base(mongoDbServiceFactory, logger)
    {
    }

    public IAsyncEnumerable<TEntity> GetAsync(FilterDefinition<TEntity> filter, Options<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public IAsyncEnumerable<ResultPage<TEntity, TKey>> GetPageAsync(Expression<Func<TEntity, bool>> predicate, Options<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<TEntity> GetOneAsync(FilterDefinition<TEntity> filter, OneOption<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<long> CountAsync(FilterDefinition<TEntity> filter)
    {
        throw new NotImplementedException();
    }
}

public abstract class ReadWriteDiskRepositoryCollectionBase<TEntity, TKey> : ReadOnlyDiskRepositoryCollectionBase<TEntity, TKey>, IDiskRepositoryCollection<TEntity, TKey>
    where TEntity : EntityBase<TKey>
{
    protected ReadWriteDiskRepositoryCollectionBase(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<ReadWriteDiskRepositoryCollectionBase<TEntity, TKey>> logger)
        : base(mongoDbServiceFactory, logger)
    {
    }

    public Task DropCollectionAsync()
    {
        throw new NotImplementedException();
    }

    public Task<bool> AddAsync(TEntity entity)
    {
        throw new NotImplementedException();
    }

    public Task AddManyAsync(IEnumerable<TEntity> entities)
    {
        throw new NotImplementedException();
    }

    public Task<EntityChangeResult<TEntity>> AddOrReplaceAsync(TEntity entity)
    {
        throw new NotImplementedException();
    }

    public Task<EntityChangeResult<TEntity>> UpdateOneAsync(TKey id, UpdateDefinition<TEntity> update)
    {
        throw new NotImplementedException();
    }

    public Task<TEntity> DeleteOneAsync(TKey id)
    {
        throw new NotImplementedException();
    }

    public Task<TEntity> DeleteOneAsync(Expression<Func<TEntity, bool>> predicate = null, FindOneAndDeleteOptions<TEntity, TEntity> options = default)
    {
        throw new NotImplementedException();
    }

    public Task<long> DeleteManyAsync(Expression<Func<TEntity, bool>> predicate)
    {
        throw new NotImplementedException();
    }

    public Task<long> UpdateAsync(FilterDefinition<TEntity> filter, UpdateDefinition<TEntity> update)
    {
        throw new NotImplementedException();
    }

    public Task<EntityChangeResult<TEntity>> UpdateOneAsync(FilterDefinition<TEntity> filter, UpdateDefinition<TEntity> update, FindOneAndUpdateOptions<TEntity> options = default)
    {
        throw new NotImplementedException();
    }
}
