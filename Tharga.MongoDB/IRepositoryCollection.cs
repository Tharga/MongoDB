using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Tharga.MongoDB.Disk;

namespace Tharga.MongoDB;

public interface IRepositoryCollection : IReadOnlyRepositoryCollection
{
    Task DropCollectionAsync();
}

public interface IRepositoryCollection<TEntity, TKey> : IReadOnlyRepositoryCollection<TEntity, TKey>, IRepositoryCollection
    where TEntity : EntityBase<TKey>
{
    ////Create
    //Task AddAsync(TEntity entity);
    //Task<bool> TryAddAsync(TEntity entity);
    //Task AddManyAsync(IEnumerable<TEntity> entities);

    ////Update
    //Task<EntityChangeResult<TEntity>> AddOrReplaceAsync(TEntity entity);
    //Task<EntityChangeResult<TEntity>> ReplaceOneAsync(TEntity entity, OneOption<TEntity> options = null);
    //Task<EntityChangeResult<TEntity>> ReplaceOneAsync(TEntity entity, FilterDefinition<TEntity> filter, OneOption<TEntity> options = null);

    //Task<EntityChangeResult<TEntity>> UpdateOneAsync(TKey id, UpdateDefinition<TEntity> update);
    //Task<EntityChangeResult<TEntity>> UpdateOneAsync(Expression<Func<TEntity, bool>> predicate, UpdateDefinition<TEntity> update, OneOption<TEntity> options = null);
    //Task<EntityChangeResult<TEntity>> UpdateOneAsync(FilterDefinition<TEntity> filter, UpdateDefinition<TEntity> update, OneOption<TEntity> options = null);

    //Task<long> UpdateAsync(Expression<Func<TEntity, bool>> predicate, UpdateDefinition<TEntity> update);
    //Task<long> UpdateAsync(FilterDefinition<TEntity> filter, UpdateDefinition<TEntity> update);

    ////Delete
    //Task<TEntity> DeleteOneAsync(TKey id);
    //Task<TEntity> DeleteOneAsync(Expression<Func<TEntity, bool>> predicate = null, OneOption<TEntity> options = null);
    //Task<TEntity> DeleteOneAsync(FilterDefinition<TEntity> filter, OneOption<TEntity> options = null);

    //Task<long> DeleteManyAsync(Expression<Func<TEntity, bool>> predicate);
    //Task<long> DeleteManyAsync(FilterDefinition<TEntity> filter);

    ////Other
    //[Obsolete($"Use {nameof(GetCollectionScope)} instead. This method will be deprecated.")]
    //IMongoCollection<TEntity> GetCollection();

    //Task<CollectionScope<TEntity>> GetCollectionScope(Operation operation);
}