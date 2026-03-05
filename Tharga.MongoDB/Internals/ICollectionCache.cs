using System.Collections.Generic;
using System.Threading.Tasks;

namespace Tharga.MongoDB.Internals;

internal interface ICollectionCache
{
    Task<IReadOnlyList<CollectionInfo>> LoadAllAsync();
    Task SaveAsync(CollectionInfo info);
    Task DeleteAsync(string databaseName, string collectionName);
    Task ResetAsync();
}
