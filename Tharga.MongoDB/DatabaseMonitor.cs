using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Extensions.Options;
using Tharga.MongoDB.Configuration;
using Tharga.MongoDB.Internals;

namespace Tharga.MongoDB;

internal class DatabaseMonitor : IDatabaseMonitor
{
    private readonly IMongoDbServiceFactory _mongoDbServiceFactory;
    private readonly IMongoDbInstance _mongoDbInstance;
    private readonly IServiceProvider _serviceProvider;
    private readonly IRepositoryConfiguration _repositoryConfiguration;
    private readonly DatabaseOptions _options;
    private readonly ConcurrentDictionary<(string ConfigurationName, string CollectionName), CollectionAccessData> _accessedCollections = new();

    public DatabaseMonitor(IMongoDbServiceFactory mongoDbServiceFactory, IMongoDbInstance mongoDbInstance, IServiceProvider serviceProvider, IRepositoryConfiguration repositoryConfiguration, IOptions<DatabaseOptions> options)
    {
        _mongoDbServiceFactory = mongoDbServiceFactory;
        _mongoDbInstance = mongoDbInstance;
        _serviceProvider = serviceProvider;
        _repositoryConfiguration = repositoryConfiguration;
        _options = options.Value;

        _mongoDbServiceFactory.ConfigurationAccessEvent += (_, e) =>
        {

        };
        _mongoDbServiceFactory.CollectionAccessEvent += (_, e) =>
        {
            var now = DateTime.UtcNow;
            _accessedCollections.AddOrUpdate((e.DatabaseContext.ConfigurationName.Value, e.DatabaseContext.CollectionName), new CollectionAccessData
            {
                DatabaseContext = e.DatabaseContext,
                EntityType = e.EntityType,
                FirstAccessed = now,
                LastAccessed = now,
                AccessCount = 1,
                Server = e.Server,
            }, (_, item) =>
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
    //TODO: Have the option for a wide scan for all databases/collections on the server/configuration
    public async IAsyncEnumerable<CollectionInfo> GetInstancesAsync()
    {
        var configurations = GetConfigurations().Select(x => new DatabaseContext { ConfigurationName = x.Value });
        var contexts = configurations.Union(_accessedCollections.Select(x => new DatabaseContext
            {
                ConfigurationName = x.Value.DatabaseContext.ConfigurationName,
                DatabasePart = x.Value.DatabaseContext.DatabasePart
            }))
            .Distinct()
            .ToArray();

        var collectionsFromCode = GetStaticCollectionsFromCode().ToDictionary(x => (x.ConfigurationName ?? _options.DefaultConfigurationName, x.CollectionName), x => x);
        var accessedCollections = _accessedCollections;
        var dynamicCollectionsFromCode = GetDynamicRegistrations().ToDictionary(x => (x.Type), x => x);

        foreach (var context in contexts)
        {
            await foreach (var inDatabase in GetCollectionsInDatabase(context))
            {
                var item = inDatabase;

                //Map Static registrations
                if (collectionsFromCode.TryGetValue((item.ConfigurationName, item.CollectionName), out var reg))
                {
                    //AssureSame(item.CollectionTypeName, reg.CollectionTypeName);

                    item = item with
                    {
                        Source = item.Source | reg.Source,
                        Registration = reg.Registration,
                        Types = item.Types.Union(reg.Types).ToArray(),
                        CollectionTypeName = item.CollectionTypeName ?? reg.CollectionTypeName
                    };
                    //TODO: Append information about registered indexes (so that we can compare with actual indexes)
                }
                else
                {
                    //NOTE: This collection exists but is not statically defined in code. (Perhaps it is dynamic or there is no code for it.)
                }

                //Map Accessed collections
                if (accessedCollections.TryGetValue((inDatabase.ConfigurationName, inDatabase.CollectionName), out var t))
                {
                    item = item with
                    {
                        Source = item.Source | Source.Monitor,
                        AccessCount = t.AccessCount,
                        Types = item.Types.Union([t.EntityType.Name]).ToArray(),
                        CollectionTypeName = item.CollectionTypeName, // ?? t.CollectionType.Name
                    };
                }
                else
                {
                    //NOTE: Collection has never been accessed.
                }

                //Map Dynamic registrations
                if (dynamicCollectionsFromCode.TryGetValue(inDatabase.Types.Single(), out var dyn))
                {
                    //AssureSame(item.CollectionTypeName, dyn.CollectionTypeName);

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
    }

    private IEnumerable<StatColInfo> GetStaticCollectionsFromCode()
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

                var item = new StatColInfo
                {
                    Source = Source.Registration,
                    ConfigurationName = instance.ConfigurationName,
                    CollectionName = instance.CollectionName,
                    Types = [genericParam?.Name],
                    CollectionTypeName = registeredCollection.Key.Name,
                    Registration = Registration.Static
                };

                yield return item;
            }
        }
    }

    private IEnumerable<DynColInfo> GetDynamicRegistrations()
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
                    var item = new DynColInfo
                    {
                        Source = Source.Registration,
                        Type = genericParam.Name,
                        CollectionTypeName = registeredCollection.Key.Name
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

    private async IAsyncEnumerable<CollectionInfo> GetCollectionsInDatabase(DatabaseContext databaseContext)
    {
        var factory = _mongoDbServiceFactory.GetMongoDbService(() => databaseContext);
        var collections = await factory.GetCollectionsWithMetaAsync().ToArrayAsync();
        foreach (var collection in collections)
        {
            yield return new CollectionInfo
            {
                Source = Source.Database,
                ConfigurationName = collection.ConfigurationName,
                Server = collection.Server,
                DatabaseName = collection.DatabaseName,
                CollectionName = collection.CollectionName,
                DocumentCount = collection.DocumentCount,
                Size = collection.Size,
                Types = collection.Types,
                //Indexes = collection.Indexes,
            };
        }
    }

    //private void AssureSame(string first, string second)
    //{
    //    if (first == null) return;
    //    if (second == null) return;
    //    if (first != second) throw new InvalidOperationException($"Invalid difference between {first} and {second}.");
    //}

    internal abstract record ColInfo
    {
        public required Source Source { get; init; }
        public required string CollectionTypeName { get; init; }
    }

    internal record StatColInfo : ColInfo
    {
        public required string ConfigurationName { get; init; }
        public required string CollectionName { get; init; }
        public required Registration Registration { get; init; }
        public required string[] Types { get; init; }
    }

    internal record DynColInfo : ColInfo
    {
        public required string Type { get; init; }
    }
}