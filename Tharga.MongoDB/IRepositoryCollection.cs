using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Driver;

namespace Tharga.MongoDB;

public interface IReadOnlyRepositoryCollection
{
    Task<long> GetSizeAsync();
}

public interface IRepositoryCollection : IReadOnlyRepositoryCollection
{
    Task DropCollectionAsync();
}

public interface IReadOnlyRepositoryCollection<TEntity, in TKey> : IReadOnlyRepositoryCollection
    where TEntity : EntityBase<TKey>
{
    IAsyncEnumerable<TEntity> GetAsync(Expression<Func<TEntity, bool>> predicate = null, Options<TEntity> options = null, CancellationToken cancellationToken = default);
    IAsyncEnumerable<T> GetAsync<T>(Expression<Func<T, bool>> predicate = null, Options<T> options = null, CancellationToken cancellationToken = default) where T : TEntity;
    Task<TEntity> GetOneAsync(TKey id, CancellationToken cancellationToken = default);
    Task<TEntity> GetOneAsync(Expression<Func<TEntity, bool>> predicate = null, OneOption<TEntity> options = null, CancellationToken cancellationToken = default);
    Task<T> GetOneAsync<T>(Expression<Func<T, bool>> predicate = null, OneOption<T> options = null, CancellationToken cancellationToken = default) where T : TEntity;
    Task<long> CountAsync(Expression<Func<TEntity, bool>> predicate);
}

public interface IRepositoryCollection<TEntity, in TKey> : IReadOnlyRepositoryCollection<TEntity, TKey>, IRepositoryCollection
    where TEntity : EntityBase<TKey>
{
    Task<bool> AddAsync(TEntity entity);
    Task AddManyAsync(IEnumerable<TEntity> entities);
    Task<EntityChangeResult<TEntity>> AddOrReplaceAsync(TEntity entity);
    Task<EntityChangeResult<TEntity>> UpdateOneAsync(TKey id, UpdateDefinition<TEntity> update);
    Task<TEntity> DeleteOneAsync(TKey id);
    Task<TEntity> DeleteOneAsync(Expression<Func<TEntity, bool>> predicate = null, FindOneAndDeleteOptions<TEntity, TEntity> options = default);
    Task<long> DeleteManyAsync(Expression<Func<TEntity, bool>> predicate);
}

public interface IReadOnlyDiskRepositoryCollection<TEntity, TKey> : IReadOnlyRepositoryCollection<TEntity, TKey>
    where TEntity : EntityBase<TKey>
{
    IAsyncEnumerable<TEntity> GetAsync(FilterDefinition<TEntity> filter, Options<TEntity> options = null, CancellationToken cancellationToken = default);
    IAsyncEnumerable<ResultPage<TEntity, TKey>> GetPageAsync(Expression<Func<TEntity, bool>> predicate, Options<TEntity> options = null, CancellationToken cancellationToken = default);
    Task<TEntity> GetOneAsync(FilterDefinition<TEntity> filter, OneOption<TEntity> options = null, CancellationToken cancellationToken = default);
    Task<long> CountAsync(FilterDefinition<TEntity> filter);
}

public interface IDiskRepositoryCollection<TEntity, TKey> : IRepositoryCollection<TEntity, TKey>, IReadOnlyDiskRepositoryCollection<TEntity, TKey>
    where TEntity : EntityBase<TKey>
{
    Task<long> UpdateAsync(FilterDefinition<TEntity> filter, UpdateDefinition<TEntity> update);
    Task<EntityChangeResult<TEntity>> UpdateOneAsync(FilterDefinition<TEntity> filter, UpdateDefinition<TEntity> update, FindOneAndUpdateOptions<TEntity> options = default);
}