using System;

namespace Tharga.MongoDB;

public interface ICollectionProviderCache
{
    TCollection GetCollection<TCollection>(DatabaseContext databaseContext, Func<DatabaseContext, TCollection> loader) where TCollection : IReadOnlyRepositoryCollection;
}