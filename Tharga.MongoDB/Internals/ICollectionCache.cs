using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Tharga.MongoDB.Internals;

internal interface ICollectionCache
{
    bool TryGet(string key, out CollectionInfo value);
    IEnumerable<CollectionInfo> GetAll();

    CollectionInfo AddOrUpdate(string key,
        Func<string, CollectionInfo> addValueFactory,
        Func<string, CollectionInfo, CollectionInfo> updateValueFactory);

    bool Remove(string key, out CollectionInfo value);

    Task InitializeAsync();
    Task SaveAsync(CollectionInfo info);
    Task DeleteAsync(string databaseName, string collectionName);
    Task ResetAsync();
}
