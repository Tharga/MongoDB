using System;

namespace Tharga.MongoDB.Internals;

internal class CollectionProviderNoCache : ICollectionProviderCache
{
    public TCollection GetCollection<TCollection>(DatabaseContext databaseContext, Func<DatabaseContext, TCollection> loader)
        where TCollection : IReadOnlyRepositoryCollection
    {
        return loader.Invoke(databaseContext);
    }
}