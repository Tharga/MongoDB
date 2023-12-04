using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Driver;

namespace Tharga.MongoDB;

public interface IRepositoryCollection
{
    Task DropCollectionAsync();
    Task<long> GetSizeAsync();
}

public interface IRepositoryCollection<TEntity, TKey> : IRepositoryCollection
    where TEntity : EntityBase<TKey>
{
    IAsyncEnumerable<TEntity> GetAsync(Expression<Func<TEntity, bool>> predicate = null, Options<TEntity> options = null, CancellationToken cancellationToken = default);
    IAsyncEnumerable<TEntity> GetAsync(FilterDefinition<TEntity> filter, Options<TEntity> options = null, CancellationToken cancellationToken = default);
    IAsyncEnumerable<T> GetAsync<T>(Expression<Func<T, bool>> predicate = null, Options<T> options = null, CancellationToken cancellationToken = default) where T : TEntity;
    IAsyncEnumerable<ResultPage<TEntity, TKey>> GetPagesAsync(Expression<Func<TEntity, bool>> predicate, Options<TEntity> options = null, CancellationToken cancellationToken = default);
    Task<TEntity> GetOneAsync(TKey id, CancellationToken cancellationToken = default);
    Task<TEntity> GetOneAsync(Expression<Func<TEntity, bool>> predicate = null, OneOption<TEntity> options = null, CancellationToken cancellationToken = default);
    Task<TEntity> GetOneAsync(FilterDefinition<TEntity> filter, OneOption<TEntity> options = null, CancellationToken cancellationToken = default);
    Task<T> GetOneAsync<T>(Expression<Func<T, bool>> predicate = null, OneOption<T> options = null, CancellationToken cancellationToken = default) where T : TEntity;
    Task AddAsync(TEntity entity);
    Task<bool> TryAddAsync(TEntity entity);
    Task AddManyAsync(IEnumerable<TEntity> entities);
    Task<EntityChangeResult<TEntity>> AddOrReplaceAsync(TEntity entity);
    Task<EntityChangeResult<TEntity>> ReplaceOneAsync(TEntity entity, OneOption<TEntity> options = null);
    Task<EntityChangeResult<TEntity>> UpdateOneAsync(TKey id, UpdateDefinition<TEntity> update);
    //TODO: When we are sure the UpdateOneAsync with OneOption works as intended, deprecate this one.
    Task<EntityChangeResult<TEntity>> UpdateOneAsync(FilterDefinition<TEntity> filter, UpdateDefinition<TEntity> update, FindOneAndUpdateOptions<TEntity> options = default);
    Task<EntityChangeResult<TEntity>> UpdateOneAsync(FilterDefinition<TEntity> filter, UpdateDefinition<TEntity> update, OneOption<TEntity> options);
    Task<TEntity> DeleteOneAsync(TKey id);
    //TODO: When we are sure the DeleteOneAsync with OneOption works as intended, deprecate this one.
    Task<TEntity> DeleteOneAsync(Expression<Func<TEntity, bool>> predicate, FindOneAndDeleteOptions<TEntity, TEntity> options);
    Task<TEntity> DeleteOneAsync(Expression<Func<TEntity, bool>> predicate = null, OneOption<TEntity> options = null);
    Task<long> DeleteManyAsync(Expression<Func<TEntity, bool>> predicate);
    Task<long> CountAsync(Expression<Func<TEntity, bool>> predicate);
    Task<long> CountAsync(FilterDefinition<TEntity> filter);
}