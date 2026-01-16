using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Tharga.MongoDB.Disk;

namespace Tharga.MongoDB;

public interface IDiskRepositoryCollection<TEntity, TKey> : IDiskReadOnlyRepositoryCollection<TEntity, TKey>
    where TEntity : EntityBase<TKey>
{
    //Create
    Task AddAsync(TEntity entity);
    Task<bool> TryAddAsync(TEntity entity);
    Task AddManyAsync(IEnumerable<TEntity> entities);

    //Update
    Task<EntityChangeResult<TEntity>> AddOrReplaceAsync(TEntity entity);
    Task<EntityChangeResult<TEntity>> ReplaceOneAsync(TEntity entity, OneOption<TEntity> options = null);
    //Task<EntityChangeResult<TEntity>> ReplaceOneAsync(TEntity entity, FilterDefinition<TEntity> filter, OneOption<TEntity> options = null);

    Task<EntityChangeResult<TEntity>> UpdateOneAsync(TKey id, UpdateDefinition<TEntity> update);
    Task<EntityChangeResult<TEntity>> UpdateOneAsync(Expression<Func<TEntity, bool>> predicate, UpdateDefinition<TEntity> update, OneOption<TEntity> options = null);
    Task<EntityChangeResult<TEntity>> UpdateOneAsync(FilterDefinition<TEntity> filter, UpdateDefinition<TEntity> update, OneOption<TEntity> options = null);

    Task<long> UpdateAsync(Expression<Func<TEntity, bool>> predicate, UpdateDefinition<TEntity> update);

    //Other
    [Obsolete($"Use {nameof(ExecuteAsync)} instead. This method will be deprecated.")]
    IMongoCollection<TEntity> GetCollection();
    Task<T> ExecuteAsync<T>(Func<IMongoCollection<TEntity>, Task<T>> execute, Operation operation);
    Task<T> ExecuteAsync<T>(Func<IMongoCollection<TEntity>, CancellationToken, Task<T>> execute, Operation operation, CancellationToken cancellationToken);
}

public interface IDiskRepositoryCollection<TEntity> : IDiskRepositoryCollection<TEntity, ObjectId>
    where TEntity : EntityBase<ObjectId>;