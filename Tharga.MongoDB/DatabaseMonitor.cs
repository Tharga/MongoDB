using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Tharga.MongoDB.Configuration;
using Tharga.MongoDB.Disk;
using Tharga.MongoDB.Internals;

namespace Tharga.MongoDB;

internal class DatabaseMonitor : IDatabaseMonitor
{
    private readonly IMongoDbServiceFactory _mongoDbServiceFactory;
    private readonly IMongoDbInstance _mongoDbInstance;
    private readonly IServiceProvider _serviceProvider;
    private readonly IRepositoryConfiguration _repositoryConfiguration;
    private readonly ICollectionProvider _collectionProvider;
    private readonly DatabaseOptions _options;
    private readonly ConcurrentDictionary<(string ConfigurationName, string CollectionName), CollectionAccessData> _accessedCollections = new();

    public DatabaseMonitor(IMongoDbServiceFactory mongoDbServiceFactory, IMongoDbInstance mongoDbInstance, IServiceProvider serviceProvider, IRepositoryConfiguration repositoryConfiguration, ICollectionProvider collectionProvider, IOptions<DatabaseOptions> options)
    {
        _mongoDbServiceFactory = mongoDbServiceFactory;
        _mongoDbInstance = mongoDbInstance;
        _serviceProvider = serviceProvider;
        _repositoryConfiguration = repositoryConfiguration;
        _collectionProvider = collectionProvider;
        _options = options.Value;

        _mongoDbServiceFactory.ConfigurationAccessEvent += (_, _) =>
        {
        };
        _mongoDbServiceFactory.CollectionAccessEvent += (_, e) =>
        {
            var databaseContext = e.DatabaseContext with
            {
                ConfigurationName = e.DatabaseContext.ConfigurationName?.Value ?? _options.DefaultConfigurationName.Value,
                CollectionName = e.DatabaseContext.CollectionName ?? e.CollectionName
            };

            var now = DateTime.UtcNow;
            _accessedCollections.AddOrUpdate((databaseContext.ConfigurationName?.Value, e.CollectionName), new CollectionAccessData
            {
                DatabaseContext = databaseContext,
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

    //TODO: Should return information about database clean.
    public async IAsyncEnumerable<CollectionInfo> GetInstancesAsync(bool fullDatabaseScan)
    {
        var configurations = GetConfigurations().Select(x => new DatabaseContext { ConfigurationName = x.Value ?? _options.DefaultConfigurationName.Value }).ToArray();
        var contexts = configurations.Union(_accessedCollections.Select(x => new DatabaseContext
            {
                ConfigurationName = x.Value.DatabaseContext.ConfigurationName?.Value ?? _options.DefaultConfigurationName.Value,
                DatabasePart = x.Value.DatabaseContext.DatabasePart
            }))
            .Distinct()
            .ToArray();

        var collectionsFromCode = GetStaticCollectionsFromCode().ToDictionary(x => (x.ConfigurationName ?? _options.DefaultConfigurationName.Value, x.CollectionName), x => x);
        var accessedCollections = _accessedCollections;
        var dynamicCollectionsFromCode = GetDynamicRegistrations().ToDictionary(x => (x.Type), x => x);

        foreach (var context in contexts)
        {
            await foreach (var inDatabase in GetCollectionsInDatabase(context, fullDatabaseScan))
            {
                var item = inDatabase;

                //Map Static registrations
                if (collectionsFromCode.TryGetValue((item.ConfigurationName, item.CollectionName), out var reg))
                {
                    //TODO: Add static DatabaseContext-part here.

                    item = item with
                    {
                        Source = item.Source | reg.Source,
                        Registration = reg.Registration,
                        Types = item.Types.Union(reg.Types).ToArray(),
                        CollectionTypeName = item.CollectionTypeName ?? reg.CollectionTypeName,
                        Index = item.Index with
                        {
                            Current = item.Index.Current,
                            Defined = reg.DefinedIndices.ToArray()
                        }
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
                        Context = item.Context ?? t.DatabaseContext,
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
                if (inDatabase.Types.Length == 1)
                {
                    if (dynamicCollectionsFromCode.TryGetValue(inDatabase.Types.Single(), out var dyn))
                    {
                        //TODO: Check if this is from the same configuration (server)
                        //TODO: Can a dynamic DatabaseContext-part be found here?

                        //AssureSame(item.CollectionTypeName, dyn.CollectionTypeName);

                        item = item with
                        {
                            Source = item.Source | dyn.Source,
                            Registration = Registration.Dynamic,
                            CollectionTypeName = item.CollectionTypeName ?? dyn.CollectionTypeName,
                            Index = item.Index with
                            {
                                Current = item.Index.Current,
                                Defined = dyn.DefinedIndices.ToArray()
                            }
                        };
                        //TODO: Append information about registered indexes (so that we can compare with actual indexes)
                    }
                    else
                    {
                        //TODO:?
                    }
                }
                else
                {
                    //TODO: ?
                }

                yield return item;
            }
        }
    }

    public async Task DropIndexAsync(DatabaseContext databaseContext)
    {
        var collection = _collectionProvider.GetCollection<DRC, DRCE>(databaseContext);
        await collection.DropIndex(collection.GetCollection());
    }

    public async Task RestoreIndexAsync(DatabaseContext databaseContext)
    {
        var collections = await GetInstancesAsync(false)
            .Where(x => x.ConfigurationName == databaseContext.ConfigurationName.Value)
            .Where(x => x.CollectionName == databaseContext.CollectionName)
            .ToArrayAsync();
        var col = collections.Single();

        var colType = _mongoDbInstance.RegisteredCollections.FirstOrDefault(x => x.Key.Name == col.CollectionTypeName).Key;
        var collection = _collectionProvider.GetCollection(colType);

        var collectionType = collection.GetType();
        var collectionMethod = collectionType.GetMethod("GetCollection");
        if (collectionMethod == null) throw new NullReferenceException("Cannot find 'GetCollection' method.");
        var collectionInstance = collectionMethod?.Invoke(collection, []);

        var indexMethod = collectionType.GetMethod("AssureIndex", BindingFlags.NonPublic | BindingFlags.Instance);
        if (indexMethod == null) throw new NullReferenceException("Cannot find 'AssureIndex' method.");

        var task = (Task)indexMethod.Invoke(collection, [collectionInstance, true, true])!;
        await task;
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
                    Registration = Registration.Static,
                    DefinedIndices = instance.BuildIndexMetas().ToArray()
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

                //NOTE: Create an instance and find the collection name.
                var colType = _mongoDbInstance.RegisteredCollections.FirstOrDefault(x => x.Key.Name == registeredCollection.Key.Name).Key;
                var collection = _collectionProvider.GetCollection(colType, new DatabaseContext()) as RepositoryCollectionBase;

                if (genericParam?.Name != null)
                {
                    var item = new DynColInfo
                    {
                        Source = Source.Registration,
                        Type = genericParam.Name,
                        CollectionTypeName = registeredCollection.Key.Name,
                        DefinedIndices = collection.BuildIndexMetas().ToArray()
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

    private async IAsyncEnumerable<CollectionInfo> GetCollectionsInDatabase(DatabaseContext databaseContext, bool fullDatabaseScan)
    {
        var mongoDbService = _mongoDbServiceFactory.GetMongoDbService(() => databaseContext);

        if (fullDatabaseScan)
        {
            var databases = mongoDbService.GetDatabases().ToArray();
            foreach (var database in databases)
            {
                var collections = await mongoDbService.GetCollectionsWithMetaAsync(database).ToArrayAsync();
                foreach (var collection in collections)
                {
                    yield return BuildCollectionInfo(collection);
                }
            }
        }
        else
        {
            var collections = await mongoDbService.GetCollectionsWithMetaAsync().ToArrayAsync();
            foreach (var collection in collections)
            {
                yield return BuildCollectionInfo(collection);
            }
        }
    }

    private static CollectionInfo BuildCollectionInfo(CollectionMeta collection)
    {
        return new CollectionInfo
        {
            ConfigurationName = collection.ConfigurationName,
            Server = collection.Server,
            DatabaseName = collection.DatabaseName,
            CollectionName = collection.CollectionName,
            Source = Source.Database,

            //--> Revisit

            DocumentCount = collection.DocumentCount,
            Size = collection.Size,
            Types = collection.Types,
            Index = new IndexInfo
            {
                Current = [..collection.Indexes],
                Defined = null
            }
        };
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
        public required IndexMeta[] DefinedIndices { get; init; }
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