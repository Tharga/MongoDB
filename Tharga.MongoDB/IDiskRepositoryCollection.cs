using System;
using MongoDB.Bson;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using MongoDB.Driver;
using Tharga.MongoDB.Disk;

namespace Tharga.MongoDB;

public interface IDiskRepositoryCollection<TEntity, TKey> : IDiskReadOnlyRepositoryCollection<TEntity, TKey>
    where TEntity : EntityBase<TKey>
{
    //Create
    Task AddManyAsync(IEnumerable<TEntity> entities);

    //Update
    Task<EntityChangeResult<TEntity>> AddOrReplaceAsync(TEntity entity);
    Task<EntityChangeResult<TEntity>> ReplaceOneAsync(TEntity entity, OneOption<TEntity> options = null);
    Task<EntityChangeResult<TEntity>> ReplaceOneAsync(TEntity entity, FilterDefinition<TEntity> filter, OneOption<TEntity> options = null);

    Task<EntityChangeResult<TEntity>> UpdateOneAsync(TKey id, UpdateDefinition<TEntity> update);
    Task<EntityChangeResult<TEntity>> UpdateOneAsync(Expression<Func<TEntity, bool>> predicate, UpdateDefinition<TEntity> update, OneOption<TEntity> options = null);
    Task<EntityChangeResult<TEntity>> UpdateOneAsync(FilterDefinition<TEntity> filter, UpdateDefinition<TEntity> update, OneOption<TEntity> options = null);

    Task<long> UpdateAsync(Expression<Func<TEntity, bool>> predicate, UpdateDefinition<TEntity> update);

    //Other
    [Obsolete($"Use {nameof(GetCollectionScope)} instead. This method will be deprecated.")]
    IMongoCollection<TEntity> GetCollection();
    Task<CollectionScope<TEntity>> GetCollectionScope(Operation operation);
}

public interface IDiskRepositoryCollection<TEntity> : IRepositoryCollection<TEntity, ObjectId>
    where TEntity : EntityBase<ObjectId>;