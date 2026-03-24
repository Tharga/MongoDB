using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Tharga.MongoDB.Internals;

internal interface ICollectionCache
{
    bool TryGet(string key, out CollectionInfo value);
    CollectionInfo AddOrUpdate(string key, Func<string, CollectionInfo> addFactory, Func<string, CollectionInfo, CollectionInfo> updateFactory);
    bool TryRemove(string key, out CollectionInfo value);
    void Set(string key, CollectionInfo value);
    IEnumerable<CollectionInfo> GetAll();
    IEnumerable<string> GetKeys();
    void Clear();

    Task LoadAsync();
    Task SaveAsync(CollectionInfo info);
    Task DeleteAsync(string databaseName, string collectionName);
    Task ResetAsync();
}
