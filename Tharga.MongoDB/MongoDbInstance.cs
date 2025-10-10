using System;
using System.Collections.Concurrent;

namespace Tharga.MongoDB;

internal class MongoDbInstance : IMongoDbInstance
{
    public ConcurrentDictionary<Type, Type> RegisteredRepositories { get; } = new();
    public ConcurrentDictionary<Type, Type> RegisteredCollections { get; } = new();
}