using System.Collections.Generic;
using System.Threading.Tasks;

namespace Tharga.MongoDB.Internals;

internal class MemoryCollectionCache : ICollectionCache
{
    public Task<IReadOnlyList<CollectionInfo>> LoadAllAsync() => Task.FromResult<IReadOnlyList<CollectionInfo>>([]);

    public Task SaveAsync(CollectionInfo info) => Task.CompletedTask;

    public Task DeleteAsync(string databaseName, string collectionName) => Task.CompletedTask;

    public Task ResetAsync() => Task.CompletedTask;
}
