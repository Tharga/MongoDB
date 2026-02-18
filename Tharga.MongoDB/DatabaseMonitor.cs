using Microsoft.Extensions.Logging;
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
    private readonly ICallLibrary _callLibrary;
    private readonly ILogger<DatabaseMonitor> _logger;
    private readonly DatabaseOptions _options;
    private readonly ConcurrentDictionary<string, CollectionAccessData> _accessedCollections = new();
    private bool _started;

    public event EventHandler<CollectionInfoChangedEventArgs> CollectionInfoChangedEvent;
    public event EventHandler<CollectionDroppedEventArgs> CollectionDroppedEvent;

    public DatabaseMonitor(IMongoDbServiceFactory mongoDbServiceFactory, IMongoDbInstance mongoDbInstance, IServiceProvider serviceProvider, IRepositoryConfiguration repositoryConfiguration, ICollectionProvider collectionProvider, ICallLibrary callLibrary, IOptions<DatabaseOptions> options, ILogger<DatabaseMonitor> logger)
    {
        _mongoDbServiceFactory = mongoDbServiceFactory;
        _mongoDbInstance = mongoDbInstance;
        _serviceProvider = serviceProvider;
        _repositoryConfiguration = repositoryConfiguration;
        _collectionProvider = collectionProvider;
        _callLibrary = callLibrary;
        _logger = logger;
        _options = options.Value;
    }

    internal void Start()
    {
        if (_started) throw new InvalidOperationException($"{nameof(DatabaseMonitor)} has already been started.");

        try
        {
            _mongoDbServiceFactory.CollectionAccessEvent += async (_, e) =>
            {
                try
                {
                    _logger.LogTrace($"{nameof(IMongoDbServiceFactory.CollectionAccessEvent)}: {e.Fingerprint}");

                    var now = DateTime.UtcNow;
                    _accessedCollections.AddOrUpdate(e.Fingerprint.Key, new CollectionAccessData
                    {
                        ConfigurationName = e.Fingerprint.ConfigurationName,
                        DatabaseName = e.Fingerprint.DatabaseName,
                        CollectionName = e.Fingerprint.CollectionName,
                        Server = e.Server,
                        DatabasePart = e.DatabasePart.NullIfEmpty(),

                        FirstAccessed = now,
                        LastAccessed = now,
                        AccessCount = 1,
                        EntityTypes = [e.EntityType],
                    }, (_, item) =>
                    {
                        item.LastAccessed = now;
                        item.AccessCount++;
                        item.EntityTypes = item.EntityTypes.Union([e.EntityType]).ToArray();
                        return item;
                    });

                    if (Debugger.IsAttached)
                    {
                        if (_accessedCollections.TryGetValue(e.Fingerprint.Key, out var item))
                        {
                            if (item.AccessCount > 1)
                            {
                                //TODO: Check why this collection was "accessed" more than once.
                            }
                        }
                    }

                    if (CollectionInfoChangedEvent != null)
                    {
                        var item = await GetInstanceAsync(e.Fingerprint);
                        if (item != null) CollectionInfoChangedEvent?.Invoke(this, new CollectionInfoChangedEventArgs(item));
                    }
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, exception.Message);
                }
            };
            _mongoDbServiceFactory.IndexUpdatedEvent += async (_, e) =>
            {
                try
                {
                    _logger.LogTrace($"{nameof(IMongoDbServiceFactory.IndexUpdatedEvent)}: {e.Fingerprint}");

                    if (CollectionInfoChangedEvent != null)
                    {
                        var item = await GetInstanceAsync(e.Fingerprint);
                        if (item != null) CollectionInfoChangedEvent?.Invoke(this, new CollectionInfoChangedEventArgs(item));
                    }
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, exception.Message);
                }
            };
            _mongoDbServiceFactory.CollectionDroppedEvent += (s, e) =>
            {
                CollectionDroppedEvent?.Invoke(s, e);
            };
            _mongoDbServiceFactory.CallStartEvent += (_, e) =>
            {
                try
                {
                    _logger.LogTrace($"{nameof(IMongoDbServiceFactory.CallStartEvent)}: {e.Fingerprint.CollectionName}");

                    _callLibrary.StartCall(e);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, exception.Message);
                }
            };
            _mongoDbServiceFactory.CallEndEvent += async (_, e) =>
            {
                try
                {
                    _logger.LogTrace($"{nameof(IMongoDbServiceFactory.CallEndEvent)}: {e.Elapsed}");

                    var fingerprint = await _callLibrary.EndCallAsync(e);
                    if (fingerprint == null) return;

                    if (_accessedCollections.TryGetValue(fingerprint.Key, out var data))
                    {
                        var updated = data with { CallCount = data.CallCount + 1 };
                        _accessedCollections.TryUpdate(fingerprint.Key, updated, data);
                    }

                    if (CollectionInfoChangedEvent != null)
                    {
                        var item = await GetInstanceAsync(fingerprint);
                        if (item != null) CollectionInfoChangedEvent?.Invoke(this, new CollectionInfoChangedEventArgs(item));
                    }
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, exception.Message);
                }
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
        var item = await GetInstancesAsync(false, default).SingleOrDefaultAsync(x => x.ConfigurationName == databaseContext.ConfigurationName && x.DatabaseName == databaseContext.DatabaseName && x.CollectionName == databaseContext.CollectionName);
        if (item is { Registration: not Registration.NotInCode })
        {
            try
            {
                var collection = _collectionProvider.GetCollection(item.CollectionType, item.Registration == Registration.Dynamic ? item.ToDatabaseContext() : null);
                var ct = collection.GetType();
                var method = ct.GetMethod(nameof(DiskRepositoryCollectionBase<EntityBase>.GetCleanInfoAsync), BindingFlags.Instance | BindingFlags.NonPublic);
                if (method?.Invoke(collection, []) is Task task)
                {
                    await task.ConfigureAwait(false);
                    var resultProperty = task.GetType().GetProperty(nameof(Task<object>.Result));
                    if (resultProperty?.GetValue(task) is CleanInfo cleanInfo)
                    {
                        item = item with { Clean = cleanInfo };
                    }
                }

                var entityType = ResolveEntityType(ct);
                if (entityType != null)
                {
                    item = item with { CurrentSchemaFingerprint = SchemaFingerprint.Generate(entityType) };
                }
            }
            catch
            {
                // Clean info is supplementary; don't fail the entire request
            }
        }

        return item;
    }

    private static Type ResolveEntityType(Type collectionType)
    {
        var type = collectionType;
        while (type != null)
        {
            if (type.IsGenericType)
            {
                var genericDef = type.GetGenericTypeDefinition();
                if (genericDef == typeof(RepositoryCollectionBase<,>)
                    || genericDef == typeof(DiskRepositoryCollectionBase<,>))
                {
                    return type.GetGenericArguments()[0];
                }
            }
            type = type.BaseType;
        }
        return null;
    }

    //TODO: Should return information about database clean.
    public async IAsyncEnumerable<CollectionInfo> GetInstancesAsync(bool fullDatabaseScan, string filter)
    {
        if (!_started) throw new InvalidOperationException($"{nameof(DatabaseMonitor)} has not been started. Call {nameof(MongoDbRegistrationExtensions.UseMongoDB)} on application start.");

        var configuredContexts = GetConfigurations().Select(x => new DatabaseContext { ConfigurationName = x.Value ?? _options.DefaultConfigurationName }).ToArray();
        var accessedDatabaseContexts = _accessedCollections.Select(x => new DatabaseContext
        {
            ConfigurationName = x.Value.ConfigurationName?.Value ?? _options.DefaultConfigurationName,
            DatabasePart = x.Value.DatabasePart.NullIfEmpty()
        });
        var contexts = configuredContexts.Union(accessedDatabaseContexts)
            .Distinct()
            .ToArray();

        var collectionsFromCode = await GetStaticCollectionsFromCode().ToDictionaryAsync(x => (x.ConfigurationName ?? _options.DefaultConfigurationName, x.CollectionName), x => x);
        var accessedCollections = _accessedCollections;
        var dynamicCollectionsFromCode = GetDynamicRegistrations().ToDictionary(x => x.Type, x => x);

        var visited = new Dictionary<string, CollectionInfo>();
        foreach (var context in contexts)
        {
            await foreach (var inDatabase in GetCollectionsInDatabase(context, fullDatabaseScan, filter))
            {
                var item = inDatabase;
                if (!visited.TryAdd($"{item.ConfigurationName}.{item.DatabaseName}.{item.CollectionName}", item)) continue;

                //Map Static registrations
                if (collectionsFromCode.TryGetValue((item.ConfigurationName, item.CollectionName), out var reg))
                {
                    AssureNotDifferent(item.CollectionType?.Name, reg.CollectionType?.Name);

                    item = item with
                    {
                        Source = item.Source | reg.Source,
                        Registration = reg.Registration,
                        Types = item.Types.Union(reg.Types).ToArray(),
                        CollectionType = item.CollectionType ?? reg.CollectionType,
                        //DocumentCount = new DocumentCount { Count = item.DocumentCount, Virtual = reg.VirtualCount },
                        DocumentCount = new DocumentCount { Count = item.DocumentCount },
                        Index = item.Index with
                        {
                            Current = item.Index.Current,
                            Defined = reg.DefinedIndices.ToArray()
                        },
                    };
                }
                else
                {
                    //NOTE: This collection exists but is not statically defined in code. (Perhaps it is dynamic or there is no code for it.)
                }

                //Map Accessed collections
                if (accessedCollections.TryGetValue(inDatabase.Key, out var t))
                {
                    //var cnt = InitiationLibrary.GetVirtualCount(inDatabase.Server, inDatabase.DatabaseName, inDatabase.CollectionName);

                    AssureNotDifferent(item.DatabasePart, t.DatabasePart);

                    item = item with
                    {
                        DatabasePart = item.DatabasePart ?? t.DatabasePart,
                        Source = item.Source | Source.Monitor,
                        Types = item.Types.Union(t.EntityTypes.Select(x => x.Name)).ToArray(),
                        AccessCount = t.AccessCount,
                        CallCount = t.CallCount,
                        //DocumentCount = new DocumentCount { Count = item.DocumentCount, Virtual = cnt },
                        DocumentCount = new DocumentCount { Count = item.DocumentCount },
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

                        AssureNotDifferent(item.CollectionType?.Name, dyn.CollectionType?.Name);

                        item = item with
                        {
                            Source = item.Source | dyn.Source,
                            Registration = Registration.Dynamic,
                            CollectionType = item.CollectionType ?? dyn.CollectionType,
                            Index = item.Index with
                            {
                                Current = item.Index.Current,
                                Defined = dyn.DefinedIndices.ToArray()
                            },
                        };
                        //TODO: Append information about registered indexes (so that we can compare with actual indexes)
                    }
                }

                yield return item;
            }
        }
    }

    private void AssureNotDifferent(string item, string other)
    {
        if (item.IsNullOrEmpty()) return;
        if (other.IsNullOrEmpty()) return;

        if (item != other) throw new InvalidOperationException($"'{item}' and '{other}' differs.");
    }

    public async Task TouchAsync(CollectionInfo collectionInfo)
    {
        if (!_started) throw new InvalidOperationException($"{nameof(DatabaseMonitor)} has not been started. Call {nameof(MongoDbRegistrationExtensions.UseMongoDB)} on application start.");
        if (collectionInfo == null) throw new ArgumentNullException(nameof(collectionInfo));
        if (collectionInfo.Registration == Registration.NotInCode) throw new InvalidOperationException($"{nameof(RestoreIndexAsync)} does not support {nameof(Registration)} {collectionInfo.Registration}.");

        var collection = _collectionProvider.GetCollection(collectionInfo.CollectionType, collectionInfo.Registration == Registration.Dynamic ? collectionInfo.ToDatabaseContext() : null);

        _ = await FetchMongoCollection(collection.GetType(), collection, true);
    }

    public async Task<(int Before, int After)> DropIndexAsync(CollectionInfo collectionInfo)
    {
        if (!_started) throw new InvalidOperationException($"{nameof(DatabaseMonitor)} has not been started. Call {nameof(MongoDbRegistrationExtensions.UseMongoDB)} on application start.");
        if (collectionInfo == null) throw new ArgumentNullException(nameof(collectionInfo));
        if (collectionInfo.Registration == Registration.NotInCode) throw new InvalidOperationException($"{nameof(RestoreIndexAsync)} does not support {nameof(Registration)} {collectionInfo.Registration}.");

        var collection = _collectionProvider.GetCollection(collectionInfo.CollectionType, collectionInfo.Registration == Registration.Dynamic ? collectionInfo.ToDatabaseContext() : null);

        var ct = collection.GetType();
        var mongoCollection = await FetchMongoCollection(ct, collection, false);

        var dropMethod = ct.GetMethod(nameof(DiskRepositoryCollectionBase<EntityBase>.DropIndex), BindingFlags.Instance | BindingFlags.NonPublic);
        var dropResult = dropMethod?.Invoke(collection, [mongoCollection]);
        var dropTask = (Task<(int Before, int After)>)dropResult;
        await dropTask!;
        return dropTask.Result;
    }

    public async Task RestoreIndexAsync(CollectionInfo collectionInfo)
    {
        if (!_started) throw new InvalidOperationException($"{nameof(DatabaseMonitor)} has not been started. Call {nameof(MongoDbRegistrationExtensions.UseMongoDB)} on application start.");
        if (collectionInfo == null) throw new ArgumentNullException(nameof(collectionInfo));
        if (collectionInfo.Registration == Registration.NotInCode) throw new InvalidOperationException($"{nameof(RestoreIndexAsync)} does not support {nameof(Registration)} {collectionInfo.Registration}.");

        var collection = _collectionProvider.GetCollection(collectionInfo.CollectionType, collectionInfo.Registration == Registration.Dynamic ? collectionInfo.ToDatabaseContext() : null);

        var ct = collection.GetType();
        var mongoCollection = await FetchMongoCollection(ct, collection, true);

        var dropMethod = ct.GetMethod(nameof(DiskRepositoryCollectionBase<EntityBase>.AssureIndex), BindingFlags.Instance | BindingFlags.NonPublic);
        var dropResult = dropMethod?.Invoke(collection, [mongoCollection, true, true]);
        var dropTask = (Task)dropResult;
        await dropTask!;
    }

    public async Task<IEnumerable<string[]>> GetIndexBlockersAsync(CollectionInfo collectionInfo, string indexName)
    {
        if (!_started) throw new InvalidOperationException($"{nameof(DatabaseMonitor)} has not been started. Call {nameof(MongoDbRegistrationExtensions.UseMongoDB)} on application start.");
        if (collectionInfo == null) throw new ArgumentNullException(nameof(collectionInfo));
        if (collectionInfo.Registration == Registration.NotInCode) throw new InvalidOperationException($"{nameof(RestoreIndexAsync)} does not support {nameof(Registration)} {collectionInfo.Registration}.");

        var collection = _collectionProvider.GetCollection(collectionInfo.CollectionType, collectionInfo.Registration == Registration.Dynamic ? collectionInfo.ToDatabaseContext() : null);

        var ct = collection.GetType();
        var mongoCollection = await FetchMongoCollection(ct, collection, true);

        var getblockerMethod = ct.GetMethod(nameof(DiskRepositoryCollectionBase<EntityBase>.GetIndexBlockers), BindingFlags.Instance | BindingFlags.NonPublic);
        //var getResult = getblockerMethod?.Invoke(collection, [mongoCollection, indexName]);
        //var getTask = (Task)getResult;
        //await getTask!;

        var taskObj = getblockerMethod?.Invoke(collection, [mongoCollection, indexName]);

        if (taskObj is not Task task)
        {
            throw new InvalidOperationException($"Invoked method did not return a {nameof(Task)}.");
        }

        await task.ConfigureAwait(false);

        var resultProperty = task.GetType().GetProperty(nameof(Task<object>.Result));
        if (resultProperty == null)
        {
            throw new InvalidOperationException("Invoked task did not have a Result property.");
        }

        if (resultProperty.GetValue(task) is not IEnumerable<string[]> result)
        {
            throw new InvalidOperationException("Invoked task result was not IEnumerable<string[]>.");
        }

        return result;
    }

    public async Task<CleanInfo> CleanAsync(CollectionInfo collectionInfo, bool cleanGuids)
    {
        if (!_started) throw new InvalidOperationException($"{nameof(DatabaseMonitor)} has not been started. Call {nameof(MongoDbRegistrationExtensions.UseMongoDB)} on application start.");
        if (collectionInfo == null) throw new ArgumentNullException(nameof(collectionInfo));
        if (collectionInfo.Registration == Registration.NotInCode) throw new InvalidOperationException($"{nameof(CleanAsync)} does not support {nameof(Registration)} {collectionInfo.Registration}.");

        var collection = _collectionProvider.GetCollection(collectionInfo.CollectionType, collectionInfo.Registration == Registration.Dynamic ? collectionInfo.ToDatabaseContext() : null);

        var ct = collection.GetType();
        var mongoCollection = await FetchMongoCollection(ct, collection, true);

        var cleanMethod = ct.GetMethod(nameof(DiskRepositoryCollectionBase<EntityBase>.CleanCollectionAsync), BindingFlags.Instance | BindingFlags.NonPublic);
        var taskObj = cleanMethod?.Invoke(collection, [mongoCollection, cleanGuids]);

        if (taskObj is not Task task)
        {
            throw new InvalidOperationException($"Invoked method did not return a {nameof(Task)}.");
        }

        await task.ConfigureAwait(false);

        var resultProperty = task.GetType().GetProperty(nameof(Task<object>.Result));
        if (resultProperty?.GetValue(task) is not CleanInfo result)
        {
            throw new InvalidOperationException("Invoked task result was not CleanInfo.");
        }

        return result;
    }

    private static async Task<object> FetchMongoCollection(Type ct, IRepositoryCollection collection, bool initiate)
    {
        var fetchMethod = ct.GetMethod(nameof(DiskRepositoryCollectionBase<EntityBase>.FetchCollectionAsync), BindingFlags.Instance | BindingFlags.NonPublic);
        var fetchResult = fetchMethod?.Invoke(collection, [initiate]);
        var fetchTask = (Task)fetchResult;
        await fetchTask!;
        var resultProperty = fetchTask.GetType().GetProperty("Result");
        var result = resultProperty!.GetValue(fetchTask);
        var valueProperty = result!.GetType().GetProperty("Value");
        var mongoDbCollection = valueProperty!.GetValue(result);
        return mongoDbCollection;
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
            case CallType.Ongoing:
                return _callLibrary.GetOngoingCalls();
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
                    CollectionType = registeredCollection.Key,
                    Registration = Registration.Static,
                    DefinedIndices = instance.BuildIndexMetas().ToArray(),
                    //VirtualCount = instance.VirtualCount
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
                        CollectionType = registeredCollection.Key,
                        DefinedIndices = collection.BuildIndexMetas().ToArray(),
                        //VirtualCount = collection?.VirtualCount
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

    private async IAsyncEnumerable<CollectionInfo> GetCollectionsInDatabase(DatabaseContext databaseContext, bool fullDatabaseScan, string filter)
    {
        var mongoDbService = _mongoDbServiceFactory.GetMongoDbService(() => databaseContext);

        if (fullDatabaseScan)
        {
            var databases = mongoDbService.GetDatabases().ToArray();
            foreach (var database in databases)
            {
                if (filter == null || database.ProtectCollectionName().Contains(filter))
                {
                    var collections = await mongoDbService.GetCollectionsWithMetaAsync(database).ToArrayAsync();
                    foreach (var collection in collections)
                    {
                        yield return BuildCollectionInfo(collection);
                    }
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
            DatabasePart = null,
            Source = Source.Database,
            Types = collection.Types,
            DocumentCount = new DocumentCount
            {
                Count = collection.DocumentCount
            },
            Size = collection.Size,
            Index = new IndexInfo
            {
                Current =
                [
                    ..collection.Indexes
                ],
                Defined = null
            },
            Registration = Registration.NotInCode,
            CollectionType = null
        };
    }

    internal abstract record ColInfo
    {
        public required Source Source { get; init; }
        public required Type CollectionType { get; init; }
        public required IndexMeta[] DefinedIndices { get; init; }
        //public required long? VirtualCount { get; init; }
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