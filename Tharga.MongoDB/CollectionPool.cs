using System;
using System.Collections.Concurrent;
using MongoDB.Driver;

namespace Tharga.MongoDB;

internal class CollectionPool : ICollectionPool
{
    private readonly ConcurrentDictionary<string, object> _collections = new();

    public void AddCollection<TEntity>(string fullName, TEntity collection)
    {
        if (!_collections.TryAdd(fullName, collection)) throw new InvalidOperationException($"Cannot add collection '{fullName}' to the collection pool, it has already been added. This call should be protected and thread safe. Something is wrong in the Tharga.MongoDB package.");
    }

    public bool TryGetCollection<TEntity>(string fullName, out IMongoCollection<TEntity> collection)
    {
        collection = default;
        if (!_collections.TryGetValue(fullName, out var item)) return false;

        collection = (IMongoCollection<TEntity>)item;
        return true;
    }
}