using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace Tharga.MongoDB.Internals;

internal class CollectionProviderCache : ICollectionProviderCache
{
    private readonly ConcurrentDictionary<string, Lazy<object>> _cache = new();

    public TCollection GetCollection<TCollection>(DatabaseContext databaseContext, Func<DatabaseContext, TCollection> loader)
        where TCollection : IReadOnlyRepositoryCollection
    {
        var key = $"{typeof(TCollection).FullName}.{databaseContext?.CollectionName}.{databaseContext?.DatabasePart}.{databaseContext?.ConfigurationName?.Value}";

        //NOTE: One Lazy per key, so the loader runs once per key and concurrent callers wait for that one instance. A lock per key, not one for the whole cache, so a loader that resolves another collection cannot deadlock.
        var lazy = _cache.GetOrAdd(key, _ => new Lazy<object>(() => loader.Invoke(databaseContext), LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            return (TCollection)lazy.Value;
        }
        catch
        {
            //NOTE: A failed load is not cached; the next call tries again.
            _cache.TryRemove(new KeyValuePair<string, Lazy<object>>(key, lazy));
            throw;
        }
    }
}