using MongoDB.Bson;

namespace Tharga.MongoDB;

public interface IDiskRepositoryCollection<TEntity, TKey> : IRepositoryCollection<TEntity, TKey>
    where TEntity : EntityBase<TKey>;

public interface IDiskRepositoryCollection<TEntity> : IRepositoryCollection<TEntity, ObjectId>
    where TEntity : EntityBase<ObjectId>;