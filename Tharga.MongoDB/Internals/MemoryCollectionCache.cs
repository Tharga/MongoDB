using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Tharga.MongoDB.Internals;

internal class MemoryCollectionCache : ICollectionCache
{
    private readonly ConcurrentDictionary<string, CollectionInfo> _dict = new();

    public bool TryGet(string key, out CollectionInfo value) => _dict.TryGetValue(key, out value);

    public IEnumerable<CollectionInfo> GetAll() => _dict.Values;

    public CollectionInfo AddOrUpdate(string key,
        Func<string, CollectionInfo> addValueFactory,
        Func<string, CollectionInfo, CollectionInfo> updateValueFactory)
        => _dict.AddOrUpdate(key, addValueFactory, updateValueFactory);

    public bool Remove(string key, out CollectionInfo value) => _dict.TryRemove(key, out value);

    public Task InitializeAsync() => Task.CompletedTask;

    public Task SaveAsync(CollectionInfo info) => Task.CompletedTask;

    public Task DeleteAsync(string databaseName, string collectionName) => Task.CompletedTask;

    public Task ResetAsync()
    {
        _dict.Clear();
        return Task.CompletedTask;
    }
}
