using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Tharga.MongoDB.Configuration;
using Tharga.MongoDB.Internals;

namespace Tharga.MongoDB;

internal class DatabaseMonitor : IDatabaseMonitor
{
    private readonly IMongoDbServiceFactory _mongoDbServiceFactory;
    private readonly IMongoDbInstance _mongoDbInstance;
    private readonly IServiceProvider _serviceProvider;
    private readonly IRepositoryConfiguration _repositoryConfiguration;
    private readonly ConcurrentDictionary<string, CollectionAccessData> _accessedCollections = new();

    public DatabaseMonitor(IMongoDbServiceFactory mongoDbServiceFactory, IMongoDbInstance mongoDbInstance, IServiceProvider serviceProvider, IRepositoryConfiguration repositoryConfiguration)
    {
        _mongoDbServiceFactory = mongoDbServiceFactory;
        _mongoDbInstance = mongoDbInstance;
        _serviceProvider = serviceProvider;
        _repositoryConfiguration = repositoryConfiguration;

        _mongoDbServiceFactory.ConfigurationAccessEvent += (_, e) =>
        {

        };
        _mongoDbServiceFactory.CollectionAccessEvent += (_, e) =>
        {
            var now = DateTime.UtcNow;
            _accessedCollections.AddOrUpdate(e.CollectionName, new CollectionAccessData { EntityType = e.EntityType, CollectionType = e.CollectionType, FirstAccessed = now, LastAccessed = now, AccessCount = 1 }, (_, item) =>
            {
                item.LastAccessed = now;
                item.AccessCount++;
                return item;
            });
        };
    }

    public IEnumerable<ConfigurationName> GetConfigurations()
    {
        foreach (var item in _repositoryConfiguration.GetDatabaseConfigurationNames())
        {
            yield return item;
        }
    }

    //TODO: Should return registered indexes and actual indexes, so that we can find differences.
    //TODO: Should return information about database clean.
    public async IAsyncEnumerable<CollectionInfo> GetInstancesAsync(ConfigurationName configurationName = null)
    {
        var databaseContext = new DatabaseContext { ConfigurationName = configurationName };

        var collectionInfos = GetStaticRegistrations().ToArray();
        var staticRegistrations = collectionInfos.ToDictionary(x => x.Name, x => x);
        var dynamicRegistrations = GetDynamicRegistrations().ToDictionary(x => x.Name, x => x);
        var accessedCollections = GetAccessed().ToDictionary(x => x.Name, x => x);

        await foreach (var inDatabase in GetCollectionsInDatabase(databaseContext))
        {
            var item = inDatabase;

            //Static registrations
            if (staticRegistrations.TryGetValue(inDatabase.Name, out var reg))
            {
                AssureSame(item.CollectionTypeName, reg.CollectionTypeName);

                item = item with
                {
                    Source = item.Source | reg.Source,
                    Registration = reg.Registration,
                    TypeNames = item.TypeNames.Union(reg.TypeNames).ToArray(),
                    CollectionTypeName = item.CollectionTypeName ?? reg.CollectionTypeName
                };
                //TODO: Append information about registered indexes (so that we can compare with actual indexes)
            }

            //Accessed collections
            if (accessedCollections.TryGetValue(inDatabase.Name, out var t))
            {
                item = item with
                {
                    Source = item.Source | t.Source,
                    AccessCount = t.AccessCount,
                    TypeNames = item.TypeNames.Union(t.TypeNames).ToArray(),
                };
            }

            //Dynamic registrations (by type)
            if (dynamicRegistrations.TryGetValue(inDatabase.TypeNames.Single(), out var dyn))
            {
                AssureSame(item.CollectionTypeName, dyn.CollectionTypeName);

                item = item with
                {
                    Source = item.Source | dyn.Source,
                    Registration = Registration.Dynamic,
                    CollectionTypeName = item.CollectionTypeName ?? dyn.CollectionTypeName
                };
                //TODO: Append information about registered indexes (so that we can compare with actual indexes)
            }

            yield return item;
        }
    }

    private IEnumerable<CollectionInfo> GetStaticRegistrations()
    {
        foreach (var registeredCollection in _mongoDbInstance.RegisteredCollections)
        {
            var isDynamic = registeredCollection.Value
                .GetConstructors()
                .Any(ctor => ctor.GetParameters()
                    .Any(param => param.ParameterType == typeof(DatabaseContext)));

            if (!isDynamic)
            {
                var genericParam = registeredCollection.Key
                    .GetInterfaces() // get all implemented interfaces
                    .Where(i => i.IsGenericType)
                    .Select(i => i.GetGenericArguments().FirstOrDefault())
                    .FirstOrDefault();
                //TODO: Get registered indexes (so we can compare with actual indexes)

                //NOTE: Create an instance and find the collection name.
                var instance = _serviceProvider.GetService(registeredCollection.Key) as RepositoryCollectionBase;
                if (instance == null) throw new InvalidOperationException($"Cannot create instance of '{registeredCollection.Key}'.");

                var item = new CollectionInfo
                {
                    Source = Source.Registration,
                    Name = instance.CollectionName,
                    TypeNames = [genericParam?.Name],
                    CollectionTypeName = registeredCollection.Key.Name,
                    Registration = Registration.Static
                };

                yield return item;
            }
        }
    }

    private IEnumerable<CollectionInfo> GetDynamicRegistrations()
    {
        foreach (var registeredCollection in _mongoDbInstance.RegisteredCollections)
        {
            var isDynamic = registeredCollection.Value
                .GetConstructors()
                .Any(ctor => ctor.GetParameters()
                    .Any(param => param.ParameterType == typeof(DatabaseContext)));

            if (isDynamic)
            {
                var genericParam = registeredCollection.Key
                    .GetInterfaces()
                    .Where(i => i.IsGenericType)
                    .Select(i => i.GetGenericArguments().FirstOrDefault())
                    .FirstOrDefault();

                if (genericParam?.Name != null)
                {
                    var item = new CollectionInfo
                    {
                        Source = Source.Registration,
                        Name = genericParam.Name,
                        TypeNames = [genericParam.Name],
                        CollectionTypeName = registeredCollection.Key.Name,
                        Registration = Registration.Dynamic
                    };

                    yield return item;
                }
                else
                {
                    Debugger.Break();
                }
            }
        }
    }

    private IEnumerable<CollectionInfo> GetAccessed()
    {
        foreach (var accessedCollection in _accessedCollections)
        {
            yield return new CollectionInfo
            {
                Source = Source.Monitor,
                Name = accessedCollection.Key,
                TypeNames = [accessedCollection.Value.EntityType.Name],
                AccessCount = accessedCollection.Value.AccessCount
            };
        }
    }

    private async IAsyncEnumerable<CollectionInfo> GetCollectionsInDatabase(DatabaseContext databaseContext)
    {
        var factory = _mongoDbServiceFactory.GetMongoDbService(() => databaseContext);
        var collections = await factory.GetCollectionsWithMetaAsync().ToArrayAsync();
        foreach (var collection in collections)
        {
            yield return new CollectionInfo
            {
                Source = Source.Database,
                Name = collection.Name,
                TypeNames = collection.Types
            };
        }
    }

    private void AssureSame(string first, string second)
    {
        if (first == null) return;
        if (second == null) return;
        if (first != second) throw new InvalidOperationException($"Invalid difference between {first} and {second}.");
    }
}