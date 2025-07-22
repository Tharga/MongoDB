using System;
using System.Collections.Concurrent;

namespace Tharga.MongoDB;

internal class MongoDbInstance : IMongoDbInstance
{
    public readonly ConcurrentDictionary<Type, Type> RegisteredRepositories = new();
    public readonly ConcurrentDictionary<Type, Type> RegisteredCollections = new();
}