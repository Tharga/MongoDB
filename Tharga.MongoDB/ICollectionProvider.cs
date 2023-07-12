using System;

namespace Tharga.MongoDB;

public interface ICollectionProvider
{
    [Obsolete($"Use GetDiskCollection<TEntity, TKey> with {nameof(DatabaseContext)} instead.")]
    IRepositoryCollection<TEntity, TKey> GetDiskCollection<TEntity, TKey>(string collectionName = null, string databaseNamePart = null)
        where TEntity : EntityBase<TKey>;

    IRepositoryCollection<TEntity, TKey> GetDiskCollection<TEntity, TKey>(DatabaseContext databaseContext = null)
        where TEntity : EntityBase<TKey>;

    [Obsolete($"Use GetBufferCollection<TEntity, TKey> with {nameof(DatabaseContext)} instead.")]
    IRepositoryCollection<TEntity, TKey> GetBufferCollection<TEntity, TKey>(string collectionName = null, string databaseNamePart = null)
        where TEntity : EntityBase<TKey>;

    IRepositoryCollection<TEntity, TKey> GetBufferCollection<TEntity, TKey>(DatabaseContext databaseContext = null)
        where TEntity : EntityBase<TKey>;

    [Obsolete($"Use GetCollection with {nameof(DatabaseContext)} instead.")]
    TCollection GetCollection<TCollection, TEntity, TKey>(string collectionName = null, string databasePart = null)
        where TCollection : IRepositoryCollection<TEntity, TKey>
        where TEntity : EntityBase<TKey>;

    TCollection GetCollection<TCollection, TEntity, TKey>(DatabaseContext databaseContext = null)
        where TCollection : IRepositoryCollection<TEntity, TKey>
        where TEntity : EntityBase<TKey>;
}