using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Tharga.MongoDB.Internals;

internal class CollectionProviderCache : ICollectionProviderCache
{
    private readonly ConcurrentDictionary<string, object> _cache = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    public TCollection GetCollection<TCollection>(DatabaseContext databaseContext, Func<DatabaseContext, TCollection> loader)
        where TCollection : IReadOnlyRepositoryCollection
    {
        var key = $"{typeof(TCollection).FullName}.{databaseContext.CollectionName}.{databaseContext.DatabasePart}.{databaseContext.ConfigurationName?.Value}";

        if (_cache.TryGetValue(key, out var collection))
        {
            return (TCollection)collection;
        }

        try
        {
            _lock.WaitAsync();

            if (_cache.TryGetValue(key, out collection))
            {
                return (TCollection)collection;
            }

            collection = loader.Invoke(databaseContext);
            _cache.TryAdd(key, collection);
            return (TCollection)collection;
        }
        finally
        {
            _lock.Release();
        }
    }
}