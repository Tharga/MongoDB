using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Driver;
using Tharga.MongoDB.Disk;

namespace Tharga.MongoDB;

public interface IRepositoryCollection
{
    //string ServerName { get; }
    //string DatabaseName { get; }
    //string CollectionName { get; }
    //bool AutoClean { get; }
    //bool CleanOnStartup { get; }
    //IEnumerable<Type> Types { get; }
    Task DropCollectionAsync();
    Task<long> GetSizeAsync();
}

public interface IRepositoryCollection<TEntity, TKey> : IRepositoryCollection
    where TEntity : EntityBase<TKey>
{
    //IEnumerable<CreateIndexModel<TEntity>> Indicies { get; }
    IAsyncEnumerable<TEntity> GetAsync(Expression<Func<TEntity, bool>> predicate, Options<TEntity> options = null, CancellationToken cancellationToken = default);
    IAsyncEnumerable<ResultPage<TEntity, TKey>> GetPageAsync(Expression<Func<TEntity, bool>> predicate, Options<TEntity> options = null, CancellationToken cancellationToken = default);
    Task<TEntity> GetOneAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);
    Task<T> GetOneAsync<T>(Expression<Func<T, bool>> predicate = null, CancellationToken cancellationToken = default) where T : TEntity;
    Task<bool> AddAsync(TEntity entity);
    Task<EntityChangeResult<TEntity>> AddOrReplaceAsync(TEntity entity);
    Task<EntityChangeResult<TEntity>> UpdateOneAsync(TKey id, UpdateDefinition<TEntity> update);
    Task<EntityChangeResult<TEntity>> UpdateOneAsync(FilterDefinition<TEntity> filter, UpdateDefinition<TEntity> update);
    Task<TEntity> DeleteOneAsync(TKey id);
    Task<TEntity> DeleteOneAsync(Expression<Func<TEntity, bool>> predicate, FindOneAndDeleteOptions<TEntity, TEntity> options = default);
    Task<long> CountAsync(Expression<Func<TEntity, bool>> predicate);
}