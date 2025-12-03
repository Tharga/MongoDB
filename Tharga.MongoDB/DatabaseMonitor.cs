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
using Tharga.MongoDB.Lockable;

namespace Tharga.MongoDB;

internal class DatabaseMonitor : IDatabaseMonitor
{
    private readonly IMongoDbServiceFactory _mongoDbServiceFactory;
    private readonly IMongoDbInstance _mongoDbInstance;
    private readonly IServiceProvider _serviceProvider;
    private readonly IRepositoryConfiguration _repositoryConfiguration;
    private readonly ICollectionProvider _collectionProvider;
    private readonly ICallLibrary _callLibrary;
    private readonly DatabaseOptions _options;
    private readonly ConcurrentDictionary<(string ConfigurationName, string CollectionName), CollectionAccessData> _accessedCollections = new();
    private bool _started;

    public DatabaseMonitor(IMongoDbServiceFactory mongoDbServiceFactory, IMongoDbInstance mongoDbInstance, IServiceProvider serviceProvider, IRepositoryConfiguration repositoryConfiguration, ICollectionProvider collectionProvider, ICallLibrary callLibrary, IOptions<DatabaseOptions> options)
    {
        _mongoDbServiceFactory = mongoDbServiceFactory;
        _mongoDbInstance = mongoDbInstance;
        _serviceProvider = serviceProvider;
        _repositoryConfiguration = repositoryConfiguration;
        _collectionProvider = collectionProvider;
        _callLibrary = callLibrary;
        _options = options.Value;
    }

    internal void Start()
    {
        if (_started) throw new InvalidOperationException($"{nameof(DatabaseMonitor)} has already been started.");

        try
        {
            _mongoDbServiceFactory.ConfigurationAccessEvent += (_, _) =>
            {
            };
            _mongoDbServiceFactory.CollectionAccessEvent += (_, e) =>
            {
                var databaseContext = (e.DatabaseContext as DatabaseContextWithFingerprint) ?? new DatabaseContextWithFingerprint
                {
                    DatabaseName = null,
                    CollectionName = e.DatabaseContext.CollectionName,
                    DatabasePart = e.DatabaseContext.DatabasePart ?? e.CollectionName,
                    ConfigurationName = e.DatabaseContext.ConfigurationName?.Value ?? _options.DefaultConfigurationName
                };

                var now = DateTime.UtcNow;
                _accessedCollections.AddOrUpdate((databaseContext.ConfigurationName?.Value, e.CollectionName), new CollectionAccessData
                {
                    DatabaseContext = databaseContext,
                    EntityType = e.EntityType,
                    FirstAccessed = now,
                    LastAccessed = now,
                    AccessCount = 1,
                    Server = e.Server
                }, (_, item) =>
                {
                    item.LastAccessed = now;
                    item.AccessCount++;
                    return item;
                });
            };
            _mongoDbServiceFactory.CallStartEvent += (_, e) =>
            {
                _callLibrary.StartCall(e);
            };
            _mongoDbServiceFactory.CallEndEvent += (_, e) =>
            {
                _callLibrary.EndCall(e);
            };
        }
        finally
        {
            _started = true;
        }
    }

    public IEnumerable<ConfigurationName> GetConfigurations()
    {
        if (!_started) throw new InvalidOperationException($"{nameof(DatabaseMonitor)} has not been started. Call {nameof(MongoDbRegistrationExtensions.UseMongoDB)} on application start.");

        foreach (var item in _repositoryConfiguration.GetDatabaseConfigurationNames())
        {
            yield return item;
        }
    }

    public async Task<CollectionInfo> GetInstanceAsync(CollectionFingerprint databaseContext)
    {
        return await GetInstancesAsync(false).SingleOrDefaultAsync(x => x.ConfigurationName == databaseContext.ConfigurationName && x.DatabaseName == databaseContext.DatabaseName && x.CollectionName == databaseContext.CollectionName);
    }

    //TODO: Should return information about database clean.
    public async IAsyncEnumerable<CollectionInfo> GetInstancesAsync(bool fullDatabaseScan)
    {
        if (!_started) throw new InvalidOperationException($"{nameof(DatabaseMonitor)} has not been started. Call {nameof(MongoDbRegistrationExtensions.UseMongoDB)} on application start.");

        var configurations = GetConfigurations().Select(x => new DatabaseContext { ConfigurationName = x.Value ?? _options.DefaultConfigurationName }).ToArray();
        var contexts = configurations.Union(_accessedCollections.Select(x => new DatabaseContext
            {
                ConfigurationName = x.Value.DatabaseContext.ConfigurationName?.Value ?? _options.DefaultConfigurationName,
                DatabasePart = x.Value.DatabaseContext.DatabasePart
            }))
            .Distinct()
            .ToArray();

        var collectionsFromCode = await GetStaticCollectionsFromCode().ToDictionaryAsync(x => (x.ConfigurationName ?? _options.DefaultConfigurationName, x.CollectionName), x => x);
        var accessedCollections = _accessedCollections;
        var dynamicCollectionsFromCode = GetDynamicRegistrations().ToDictionary(x => (x.Type), x => x);

        var visited = new Dictionary<string, CollectionInfo>();
        foreach (var context in contexts)
        {
            await foreach (var inDatabase in GetCollectionsInDatabase(context, fullDatabaseScan))
            {
                var item = inDatabase;
                if (!visited.TryAdd($"{item.ConfigurationName}.{item.DatabaseName}.{item.CollectionName}", item)) continue;

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
                        },
                        DocumentCount = new DocumentCount { Count = item.DocumentCount, Virtual = reg.VirtualCount }
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
                    var cnt = InitiationLibrary.GetVirtualCount(inDatabase.Server, inDatabase.DatabaseName, inDatabase.CollectionName);

                    item = item with
                    {
                        Context = item.Context ?? t.DatabaseContext,
                        Source = item.Source | Source.Monitor,
                        AccessCount = t.AccessCount,
                        Types = item.Types.Union([t.EntityType.Name]).ToArray(),
                        CollectionTypeName = item.CollectionTypeName,
                        DocumentCount = new DocumentCount { Count = item.DocumentCount, Virtual = cnt }
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
                            },
                            //DocumentCount = new DocumentCount { Count = item.DocumentCount, Virtual = dyn.VirtualCount }
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

    public async Task<(int Before, int After)> DropIndexAsync(IDatabaseContext context)
    {
        if (!_started) throw new InvalidOperationException($"{nameof(DatabaseMonitor)} has not been started. Call {nameof(MongoDbRegistrationExtensions.UseMongoDB)} on application start.");

        var databaseContext = ToDatabaseContext(context);

        var collection = _collectionProvider.GetCollection<EntityBaseCollection, EntityBase>(databaseContext);
        return await collection.DropIndex(collection.GetCollection());
    }

    public async Task RestoreIndexAsync(IDatabaseContext context)
    {
        if (!_started) throw new InvalidOperationException($"{nameof(DatabaseMonitor)} has not been started. Call {nameof(MongoDbRegistrationExtensions.UseMongoDB)} on application start.");

        var databaseContext = ToDatabaseContext(context);
        var databaseName = (databaseContext as DatabaseContextWithFingerprint)?.DatabaseName;

        var collections = await GetInstancesAsync(false)
            .Where(x => x.ConfigurationName == databaseContext.ConfigurationName)
            .Where(x => x.CollectionName == databaseContext.CollectionName)
            .Where(x => x.DatabaseName == databaseName)
            .ToArrayAsync();

        IRepositoryCollection collection;
        if (string.IsNullOrEmpty(databaseContext.DatabasePart)) // && string.IsNullOrEmpty(databaseName))
        {
            //NOTE: Static
            var col = collections.Single();
            var colTypes = _mongoDbInstance.RegisteredCollections.Where(x => x.Key.Name == col.CollectionTypeName).ToArray();
            var colType = colTypes.Single().Key;

            collection = _collectionProvider.GetCollection(colType);
        }
        else
        {
            //NOTE: Dynamic
            var col = collections.Single();
            var colTypes = _mongoDbInstance.RegisteredCollections.Where(x => x.Key.Name == col.CollectionTypeName).ToArray();
            var colType = colTypes.Single().Key;

            collection = _collectionProvider.GetCollection(colType, databaseContext);
        }

        var collectionType = collection.GetType();
        var collectionMethod = collectionType.GetMethod("GetCollection");
        if (collectionMethod == null) throw new NullReferenceException("Cannot find 'GetCollection' method.");
        var collectionInstance = collectionMethod.Invoke(collection, []);

        var indexMethod = collectionType.GetMethod("AssureIndex", BindingFlags.NonPublic | BindingFlags.Instance);
        if (indexMethod == null) throw new NullReferenceException($"Cannot find 'AssureIndex' method in '{collectionType.Name}'.");

        try
        {
            var result = indexMethod.Invoke(collection, [collectionInstance, true, true]);
            await (Task)result!;
        }
        catch (Exception ex)
        {
            Debugger.Break();
            Console.WriteLine(ex);
            Console.WriteLine(ex.InnerException);
            throw;
        }
    }

    public async Task TouchAsync(IDatabaseContext context)
    {
        if (!_started) throw new InvalidOperationException($"{nameof(DatabaseMonitor)} has not been started. Call {nameof(MongoDbRegistrationExtensions.UseMongoDB)} on application start.");

        //var collections = await GetInstancesAsync(false)
        //    .Where(x => x.ConfigurationName == context.ConfigurationName)
        //    .Where(x => x.CollectionName == context.CollectionName)
        //    .ToArrayAsync();

        //IRepositoryCollection collection;
        //if (collectionInfo.Registration == Registration.Static)
        //{
        //    var col = collections.Single();
        //    var colType = _mongoDbInstance.RegisteredCollections.FirstOrDefault(x => x.Key.Name == col.CollectionTypeName).Key;

        //    collection = _collectionProvider.GetCollection(colType);
        //}
        //else if (collectionInfo.Registration == Registration.Dynamic)
        //{
        //    var col = collections.First();
        //    var colType = _mongoDbInstance.RegisteredCollections.FirstOrDefault(x => x.Key.Name == col.CollectionTypeName).Key;

        //    var databaseContext = new DatabaseContextWithFingerprint
        //    {
        //        ConfigurationName = context.ConfigurationName,
        //        CollectionName = context.CollectionName,
        //        DatabaseName = context.DatabaseName,
        //    };

        //    collection = _collectionProvider.GetCollection(colType, databaseContext);
        //}
        //else
        //{
        //    throw new ArgumentOutOfRangeException(nameof(collectionInfo.Registration), $"Unknown {nameof(collectionInfo.Registration)} {collectionInfo.Registration}.");
        //}

        //var collectionType = collection.GetType();
        //var collectionMethod = collectionType.GetMethod("GetCollection");

        //if (collectionMethod == null) throw new NullReferenceException("Cannot find 'GetCollection' method.");
        //var collectionInstance = collectionMethod?.Invoke(collection, []);
        throw new NotImplementedException();
    }

    public IEnumerable<CallInfo> GetCalls(CallType callType)
    {
        if (!_started) throw new InvalidOperationException($"{nameof(DatabaseMonitor)} has not been started. Call {nameof(MongoDbRegistrationExtensions.UseMongoDB)} on application start.");

        switch (callType)
        {
            case CallType.Last:
                return _callLibrary.GetLastCalls();
            case CallType.Slow:
                return _callLibrary.GetSlowCalls();
            default:
                throw new ArgumentOutOfRangeException(nameof(callType), callType, null);
        }
    }

    private async IAsyncEnumerable<StatColInfo> GetStaticCollectionsFromCode()
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
                    DefinedIndices = instance.BuildIndexMetas().ToArray(),
                    VirtualCount = instance.VirtualCount
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
                        DefinedIndices = collection.BuildIndexMetas().ToArray(),
                        VirtualCount = collection?.VirtualCount
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
            DatabaseName = collection.DatabaseName,
            CollectionName = collection.CollectionName,
            Server = collection.Server,
            Source = Source.Database,
            DocumentCount = new DocumentCount { Count = collection.DocumentCount },

            //--> Revisit

            Size = collection.Size,
            Types = collection.Types,
            Index = new IndexInfo
            {
                Current = [..collection.Indexes],
                Defined = null
            }
        };
    }

    private static DatabaseContext ToDatabaseContext(IDatabaseContext context)
    {
        if (context is DatabaseContext databaseContext)
        {
        }
        else if (context is DatabaseContextWithFingerprint fp)
        {
            databaseContext = fp;
        }
        else if (context is CollectionFingerprint fingerprint)
        {
            databaseContext = new DatabaseContextWithFingerprint { ConfigurationName = fingerprint.ConfigurationName, DatabaseName = fingerprint.DatabaseName, CollectionName = fingerprint.CollectionName };
        }
        else
        {
            throw new InvalidOperationException($"Cannot convert {nameof(context)} to a known type.");
        }

        return databaseContext;
    }

    internal abstract record ColInfo
    {
        public required Source Source { get; init; }
        public required string CollectionTypeName { get; init; }
        public required IndexMeta[] DefinedIndices { get; init; }
        public required long? VirtualCount { get; init; }
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