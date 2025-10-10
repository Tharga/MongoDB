using System;
using System.Collections.Concurrent;

namespace Tharga.MongoDB;

internal interface IMongoDbInstance
{
    ConcurrentDictionary<Type, Type> RegisteredRepositories { get; }
    ConcurrentDictionary<Type, Type> RegisteredCollections { get; }
}