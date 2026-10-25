using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Tharga.MongoDB.Disk;

namespace Tharga.MongoDB;

public interface IDiskRepositoryCollection<TEntity, TKey> : IRepositoryCollection<TEntity, TKey>
    where TEntity : EntityBase<TKey>
{
    //Update
    Task<EntityChangeResult<TEntity>> AddOrReplaceAsync(TEntity entity, IClientSessionHandle session = null, CancellationToken cancellationToken = default);
    Task<EntityChangeResult<TEntity>> ReplaceOneAsync(TEntity entity, OneOption<TEntity> options = null, IClientSessionHandle session = null, CancellationToken cancellationToken = default);

    Task<EntityChangeResult<TEntity>> UpdateOneAsync(TKey id, UpdateDefinition<TEntity> update, IClientSessionHandle session = null, CancellationToken cancellationToken = default);
    Task<EntityChangeResult<TEntity>> UpdateOneAsync(Expression<Func<TEntity, bool>> predicate, UpdateDefinition<TEntity> update, OneOption<TEntity> options = null, IClientSessionHandle session = null, CancellationToken cancellationToken = default);
    Task<EntityChangeResult<TEntity>> UpdateOneAsync(FilterDefinition<TEntity> filter, UpdateDefinition<TEntity> update, OneOption<TEntity> options = null, IClientSessionHandle session = null, CancellationToken cancellationToken = default);

    Task<long> UpdateManyAsync(Expression<Func<TEntity, bool>> predicate, UpdateDefinition<TEntity> update, IClientSessionHandle session = null, CancellationToken cancellationToken = default);
    Task<long> UpdateManyAsync(FilterDefinition<TEntity> filter, UpdateDefinition<TEntity> update, IClientSessionHandle session = null, CancellationToken cancellationToken = default);

    //Delete
    Task<TEntity> DeleteOneAsync(Expression<Func<TEntity, bool>> predicate, OneOption<TEntity> options = null, IClientSessionHandle session = null, CancellationToken cancellationToken = default);
    Task<TEntity> DeleteOneAsync(FilterDefinition<TEntity> filter, OneOption<TEntity> options = null, IClientSessionHandle session = null, CancellationToken cancellationToken = default);

    Task<long> DeleteManyAsync(Expression<Func<TEntity, bool>> predicate = null, IClientSessionHandle session = null, CancellationToken cancellationToken = default);
    Task<long> DeleteManyAsync(FilterDefinition<TEntity> filter, IClientSessionHandle session = null, CancellationToken cancellationToken = default);

    //Other
    [Obsolete(Internals.Constants.GetCollectionObsoleteMessage)]
    IMongoCollection<TEntity> GetCollection();
}

public interface IDiskRepositoryCollection<TEntity> : IDiskRepositoryCollection<TEntity, ObjectId>
    where TEntity : EntityBase<ObjectId>;