using MongoDB.Driver;

namespace Tharga.MongoDB;

internal interface ICollectionPool
{
    void AddCollection<TEntity>(string fullName, TEntity collection);
    bool TryGetCollection<TEntity>(string fullName, out IMongoCollection<TEntity> collection);
}