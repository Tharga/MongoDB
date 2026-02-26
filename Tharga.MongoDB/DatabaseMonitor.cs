using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
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
    private readonly ConcurrentDictionary<string, CollectionInfo> _cache = new();
    private Dictionary<(string, string), StatColInfo> _staticLookup;
    private Dictionary<string, DynColInfo> _dynamicLookup;
    private readonly SemaphoreSlim _lookupLock = new(1, 1);
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
            _mongoDbServiceFactory.CollectionAccessEvent += (_, e) =>
            {
                try
                {
                    _logger.LogTrace($"{nameof(IMongoDbServiceFactory.CollectionAccessEvent)}: {e.Fingerprint}");

                    _cache.AddOrUpdate(e.Fingerprint.Key,
                        addValueFactory: _ =>
                        {
                            var entry = BuildInitialEntry(e.Fingerprint, e.Server, e.DatabasePart, e.EntityType.Name);
                            return entry with
                            {
                                AccessCount = 1,
                                Types = entry.Types.Union([e.EntityType.Name]).ToArray()
                            };
                        },
                        updateValueFactory: (_, existing) => existing with
                        {
                            AccessCount = existing.AccessCount + 1,
                            Types = existing.Types.Union([e.EntityType.Name]).ToArray(),
                            DatabasePart = existing.DatabasePart ?? e.DatabasePart.NullIfEmpty(),
                        });

                    if (CollectionInfoChangedEvent != null && _cache.TryGetValue(e.Fingerprint.Key, out var item))
                        CollectionInfoChangedEvent?.Invoke(this, new CollectionInfoChangedEventArgs(item));
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, exception.Message);
                }
            };

            _mongoDbServiceFactory.IndexUpdatedEvent += (_, e) =>
            {
                try
                {
                    _logger.LogTrace($"{nameof(IMongoDbServiceFactory.IndexUpdatedEvent)}: {e.Fingerprint}");

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var mongoDbService = GetMongoDbService(e.Fingerprint);
                            var meta = await mongoDbService
                                .GetCollectionsWithMetaAsync(collectionNameFilter: e.Fingerprint.CollectionName, includeDetails: true)
                                .FirstOrDefaultAsync();

                            if (meta == null) return;

                            _cache.AddOrUpdate(e.Fingerprint.Key,
                                addValueFactory: _ =>
                                {
                                    var entry = BuildInitialEntry(e.Fingerprint, meta.Server, null, null);
                                    return entry with { Index = BuildIndexInfo(entry, meta.Indexes) };
                                },
                                updateValueFactory: (_, existing) => existing with
                                {
                                    Index = BuildIndexInfo(existing, meta.Indexes)
                                });

                            if (CollectionInfoChangedEvent != null && _cache.TryGetValue(e.Fingerprint.Key, out var item))
                                CollectionInfoChangedEvent?.Invoke(this, new CollectionInfoChangedEventArgs(item));
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, ex.Message);
                        }
                    });
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, exception.Message);
                }
            };

            _mongoDbServiceFactory.CollectionDroppedEvent += (s, e) =>
            {
                var configName = e.DatabaseContext.ConfigurationName ?? _options.DefaultConfigurationName;
                var keysToRemove = _cache
                    .Where(kv => (kv.Value.ConfigurationName?.Value ?? _options.DefaultConfigurationName) == configName
                                 && kv.Value.CollectionName == e.DatabaseContext.CollectionName)
                    .Select(kv => kv.Key)
                    .ToList();
                foreach (var key in keysToRemove)
                    _cache.TryRemove(key, out _);

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

                    _cache.AddOrUpdate(fingerprint.Key,
                        addValueFactory: _ => BuildInitialEntry(fingerprint, null, null, null) with { CallCount = 1 },
                        updateValueFactory: (_, existing) => existing with { CallCount = existing.CallCount + 1 });

                    if (CollectionInfoChangedEvent != null && _cache.TryGetValue(fingerprint.Key, out var item))
                        CollectionInfoChangedEvent?.Invoke(this, new CollectionInfoChangedEventArgs(item));
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

    public async Task<CollectionInfo> GetInstanceAsync(CollectionFingerprint fingerprint)
    {
        if (!_started) throw new InvalidOperationException($"{nameof(DatabaseMonitor)} has not been started. Call {nameof(MongoDbRegistrationExtensions.UseMongoDB)} on application start.");

        if (_cache.TryGetValue(fingerprint.Key, out var cached))
        {
            var mongoDbService = GetMongoDbService(fingerprint);
            if (await mongoDbService.DoesCollectionExist(fingerprint.CollectionName))
                return cached;

            _cache.TryRemove(fingerprint.Key, out _);
            return null;
        }

        return await LoadAndCacheAsync(fingerprint);
    }

    public async IAsyncEnumerable<CollectionInfo> GetInstancesAsync(bool fullDatabaseScan, string filter)
    {
        if (!_started) throw new InvalidOperationException($"{nameof(DatabaseMonitor)} has not been started. Call {nameof(MongoDbRegistrationExtensions.UseMongoDB)} on application start.");

        var configuredContexts = GetConfigurations()
            .Select(x => new DatabaseContext { ConfigurationName = x.Value ?? _options.DefaultConfigurationName })
            .ToArray();
        var cachedContexts = _cache.Values.Select(x => new DatabaseContext
        {
            ConfigurationName = x.ConfigurationName?.Value ?? _options.DefaultConfigurationName,
            DatabasePart = x.DatabasePart.NullIfEmpty()
        });
        var contexts = configuredContexts.Union(cachedContexts).Distinct().ToArray();

        var sw = new Stopwatch();
        sw.Start();

        var currentDbKeys = new HashSet<string>();
        var visited = new HashSet<string>();
        var index = 0;
        var total = contexts.Length;

        foreach (var context in contexts)
        {
            Console.WriteLine($"Context {++index} of {total} {context.ConfigurationName}.{context.DatabasePart}.{context.CollectionName} [{sw.Elapsed.TotalSeconds:N0}s]");
            var mongoDbService = _mongoDbServiceFactory.GetMongoDbService(() => context);

            if (fullDatabaseScan)
            {
                foreach (var database in mongoDbService.GetDatabases())
                {
                    if (filter != null && !database.ProtectCollectionName().Contains(filter)) continue;
                    await foreach (var info in GetCollectionsFromDb(mongoDbService, database, filter, currentDbKeys, visited, sw))
                        yield return info;
                }
            }
            else
            {
                await foreach (var info in GetCollectionsFromDb(mongoDbService, null, filter, currentDbKeys, visited, sw))
                    yield return info;
            }
        }

        // Remove stale cache entries for collections no longer in DB
        foreach (var key in _cache.Keys.Where(k => !currentDbKeys.Contains(k)).ToList())
            _cache.TryRemove(key, out _);
    }

    public async Task RefreshStatsAsync(CollectionFingerprint fingerprint)
    {
        if (!_started) throw new InvalidOperationException($"{nameof(DatabaseMonitor)} has not been started. Call {nameof(MongoDbRegistrationExtensions.UseMongoDB)} on application start.");
        if (fingerprint == null) throw new ArgumentNullException(nameof(fingerprint));

        var mongoDbService = GetMongoDbService(fingerprint);
        var meta = await mongoDbService
            .GetCollectionsWithMetaAsync(collectionNameFilter: fingerprint.CollectionName, includeDetails: true)
            .FirstOrDefaultAsync();

        if (meta == null) return;

        _cache.AddOrUpdate(fingerprint.Key,
            addValueFactory: _ =>
            {
                var entry = BuildInitialEntry(fingerprint, meta.Server, null, null);
                return entry with
                {
                    DocumentCount = new DocumentCount { Count = meta.DocumentCount },
                    Size = meta.Size,
                    Index = BuildIndexInfo(entry, meta.Indexes)
                };
            },
            updateValueFactory: (_, existing) => existing with
            {
                DocumentCount = new DocumentCount { Count = meta.DocumentCount },
                Size = meta.Size,
                Index = BuildIndexInfo(existing, meta.Indexes)
            });

        if (_cache.TryGetValue(fingerprint.Key, out var updated))
            CollectionInfoChangedEvent?.Invoke(this, new CollectionInfoChangedEventArgs(updated));
    }

    public async Task TouchAsync(CollectionInfo collectionInfo)
    {
        if (!_started) throw new InvalidOperationException($"{nameof(DatabaseMonitor)} has not been started. Call {nameof(MongoDbRegistrationExtensions.UseMongoDB)} on application start.");
        if (collectionInfo == null) throw new ArgumentNullException(nameof(collectionInfo));
        if (collectionInfo.Registration == Registration.NotInCode) throw new InvalidOperationException($"{nameof(RestoreIndexAsync)} does not support {nameof(Registration)} {collectionInfo.Registration}.");

        var collection = _collectionProvider.GetCollection(collectionInfo.CollectionType, collectionInfo.Registration == Registration.Dynamic ? collectionInfo.ToDatabaseContext() : null);

        _ = await FetchMongoCollection(collection.GetType(), collection, true);

        var mongoDbService = GetMongoDbService(collectionInfo);
        var meta = await mongoDbService
            .GetCollectionsWithMetaAsync(collectionNameFilter: collectionInfo.CollectionName, includeDetails: true)
            .FirstOrDefaultAsync();

        if (meta == null) return;

        _cache.AddOrUpdate(collectionInfo.Key,
            addValueFactory: _ => collectionInfo with
            {
                DocumentCount = new DocumentCount { Count = meta.DocumentCount },
                Size = meta.Size,
                Index = BuildIndexInfo(collectionInfo, meta.Indexes)
            },
            updateValueFactory: (_, existing) => existing with
            {
                DocumentCount = new DocumentCount { Count = meta.DocumentCount },
                Size = meta.Size,
                Index = BuildIndexInfo(existing, meta.Indexes)
            });

        if (_cache.TryGetValue(collectionInfo.Key, out var updated))
            CollectionInfoChangedEvent?.Invoke(this, new CollectionInfoChangedEventArgs(updated));
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

        await UpdateIndexCacheAsync(collectionInfo);

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

        var restoreMethod = ct.GetMethod(nameof(DiskRepositoryCollectionBase<EntityBase>.AssureIndex), BindingFlags.Instance | BindingFlags.NonPublic);
        var restoreResult = restoreMethod?.Invoke(collection, [mongoCollection, true, true]);
        var restoreTask = (Task)restoreResult;
        await restoreTask!;

        await UpdateIndexCacheAsync(collectionInfo);
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
        var taskObj = getblockerMethod?.Invoke(collection, [mongoCollection, indexName]);

        if (taskObj is not Task task)
            throw new InvalidOperationException($"Invoked method did not return a {nameof(Task)}.");

        await task.ConfigureAwait(false);

        var resultProperty = task.GetType().GetProperty(nameof(Task<object>.Result));
        if (resultProperty == null)
            throw new InvalidOperationException("Invoked task did not have a Result property.");

        if (resultProperty.GetValue(task) is not IEnumerable<string[]> result)
            throw new InvalidOperationException("Invoked task result was not IEnumerable<string[]>.");

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
            throw new InvalidOperationException($"Invoked method did not return a {nameof(Task)}.");

        await task.ConfigureAwait(false);

        var resultProperty = task.GetType().GetProperty(nameof(Task<object>.Result));
        if (resultProperty?.GetValue(task) is not CleanInfo result)
            throw new InvalidOperationException("Invoked task result was not CleanInfo.");

        _cache.AddOrUpdate(collectionInfo.Key,
            addValueFactory: _ => collectionInfo with { Clean = result },
            updateValueFactory: (_, existing) => existing with { Clean = result });

        if (_cache.TryGetValue(collectionInfo.Key, out var updated))
            CollectionInfoChangedEvent?.Invoke(this, new CollectionInfoChangedEventArgs(updated));

        return result;
    }

    public IEnumerable<CallInfo> GetCalls(CallType callType)
    {
        if (!_started) throw new InvalidOperationException($"{nameof(DatabaseMonitor)} has not been started. Call {nameof(MongoDbRegistrationExtensions.UseMongoDB)} on application start.");

        return callType switch
        {
            CallType.Last => _callLibrary.GetLastCalls(),
            CallType.Slow => _callLibrary.GetSlowCalls(),
            CallType.Ongoing => _callLibrary.GetOngoingCalls(),
            _ => throw new ArgumentOutOfRangeException(nameof(callType), callType, null)
        };
    }

    // --- Private helpers ---

    private async Task<CollectionInfo> LoadAndCacheAsync(CollectionFingerprint fingerprint)
    {
        var mongoDbService = GetMongoDbService(fingerprint);
        var meta = await mongoDbService
            .GetCollectionsWithMetaAsync(collectionNameFilter: fingerprint.CollectionName, includeDetails: false)
            .FirstOrDefaultAsync();

        if (meta == null) return null;

        var entry = BuildInitialEntry(fingerprint, meta.Server, null, null);
        _cache[fingerprint.Key] = entry;
        return entry;
    }

    private CollectionInfo BuildInitialEntry(CollectionFingerprint fingerprint, string server, string databasePart, string entityTypeName)
    {
        var (staticLookup, dynamicLookup) = GetLookups();
        var configName = fingerprint.ConfigurationName?.Value ?? _options.DefaultConfigurationName;

        if (staticLookup.TryGetValue((configName, fingerprint.CollectionName), out var reg))
        {
            var entityType = ResolveEntityType(reg.CollectionType);
            return new CollectionInfo
            {
                ConfigurationName = fingerprint.ConfigurationName,
                DatabaseName = fingerprint.DatabaseName,
                CollectionName = fingerprint.CollectionName,
                Server = server ?? string.Empty,
                DatabasePart = databasePart.NullIfEmpty(),
                Source = reg.Source | Source.Database,
                Registration = reg.Registration,
                Types = reg.Types,
                CollectionType = reg.CollectionType,
                Index = new IndexInfo { Current = null, Defined = reg.DefinedIndices },
                CurrentSchemaFingerprint = entityType != null ? SchemaFingerprint.Generate(entityType) : null,
            };
        }

        if (entityTypeName != null && dynamicLookup.TryGetValue(entityTypeName, out var dyn))
        {
            var entityType = ResolveEntityType(dyn.CollectionType);
            return new CollectionInfo
            {
                ConfigurationName = fingerprint.ConfigurationName,
                DatabaseName = fingerprint.DatabaseName,
                CollectionName = fingerprint.CollectionName,
                Server = server ?? string.Empty,
                DatabasePart = databasePart.NullIfEmpty(),
                Source = dyn.Source | Source.Database,
                Registration = Registration.Dynamic,
                Types = [entityTypeName],
                CollectionType = dyn.CollectionType,
                Index = new IndexInfo { Current = null, Defined = dyn.DefinedIndices },
                CurrentSchemaFingerprint = entityType != null ? SchemaFingerprint.Generate(entityType) : null,
            };
        }

        return new CollectionInfo
        {
            ConfigurationName = fingerprint.ConfigurationName,
            DatabaseName = fingerprint.DatabaseName,
            CollectionName = fingerprint.CollectionName,
            Server = server ?? string.Empty,
            DatabasePart = databasePart.NullIfEmpty(),
            Source = Source.Database,
            Registration = Registration.NotInCode,
            Types = entityTypeName != null ? [entityTypeName] : [],
            CollectionType = null,
            Index = null,
        };
    }

    private (Dictionary<(string, string), StatColInfo> staticLookup, Dictionary<string, DynColInfo> dynamicLookup) GetLookups()
    {
        if (_staticLookup != null) return (_staticLookup, _dynamicLookup);

        _lookupLock.Wait();
        try
        {
            if (_staticLookup != null) return (_staticLookup, _dynamicLookup);

            _staticLookup = GetStaticCollectionsFromCodeCore()
                .ToDictionary(x => (x.ConfigurationName ?? _options.DefaultConfigurationName, x.CollectionName), x => x);
            _dynamicLookup = GetDynamicRegistrations()
                .ToDictionary(x => x.Type, x => x);

            return (_staticLookup, _dynamicLookup);
        }
        finally
        {
            _lookupLock.Release();
        }
    }

    private static IndexInfo BuildIndexInfo(CollectionInfo existing, IndexMeta[] currentIndexes)
    {
        var defined = existing.Index?.Defined ?? [];
        return new IndexInfo { Current = currentIndexes, Defined = defined };
    }

    private async Task UpdateIndexCacheAsync(CollectionInfo collectionInfo)
    {
        var mongoDbService = GetMongoDbService(collectionInfo);
        var meta = await mongoDbService
            .GetCollectionsWithMetaAsync(collectionNameFilter: collectionInfo.CollectionName, includeDetails: true)
            .FirstOrDefaultAsync();

        if (meta == null) return;

        _cache.AddOrUpdate(collectionInfo.Key,
            addValueFactory: _ => collectionInfo with { Index = BuildIndexInfo(collectionInfo, meta.Indexes) },
            updateValueFactory: (_, existing) => existing with { Index = BuildIndexInfo(existing, meta.Indexes) });

        if (_cache.TryGetValue(collectionInfo.Key, out var updated))
            CollectionInfoChangedEvent?.Invoke(this, new CollectionInfoChangedEventArgs(updated));
    }

    private IMongoDbService GetMongoDbService(CollectionFingerprint fingerprint)
    {
        return _mongoDbServiceFactory.GetMongoDbService(() => new DatabaseContext
        {
            ConfigurationName = fingerprint.ConfigurationName?.Value
        });
    }

    private async IAsyncEnumerable<CollectionInfo> GetCollectionsFromDb(IMongoDbService mongoDbService, string databaseName, string filter, HashSet<string> currentDbKeys, HashSet<string> visited, Stopwatch sw)
    {
        await foreach (var meta in mongoDbService.GetCollectionsWithMetaAsync(databaseName, includeDetails: false))
        {
            if (filter != null && !meta.CollectionName.ProtectCollectionName().Contains(filter)) continue;

            var key = $"{meta.ConfigurationName}.{meta.DatabaseName}.{meta.CollectionName}";
            currentDbKeys.Add(key);

            if (!visited.Add(key)) continue;

            Console.WriteLine($"- Got {meta.CollectionName} [{sw.Elapsed.TotalSeconds:N0}s]");

            if (_cache.TryGetValue(key, out var cached))
            {
                yield return cached;
            }
            else
            {
                var fp = new CollectionFingerprint
                {
                    ConfigurationName = meta.ConfigurationName,
                    DatabaseName = meta.DatabaseName,
                    CollectionName = meta.CollectionName
                };
                var entry = BuildInitialEntry(fp, meta.Server, null, null);
                _cache[key] = entry;
                yield return entry;
            }
        }
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

    private IEnumerable<StatColInfo> GetStaticCollectionsFromCodeCore()
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
                    .GetInterfaces()
                    .Where(i => i.IsGenericType)
                    .Select(i => i.GetGenericArguments().FirstOrDefault())
                    .FirstOrDefault();

                var instance = _serviceProvider.GetService(registeredCollection.Key) as RepositoryCollectionBase;
                if (instance == null) throw new InvalidOperationException($"Cannot create instance of '{registeredCollection.Key}'.");

                yield return new StatColInfo
                {
                    Source = Source.Registration,
                    ConfigurationName = instance.ConfigurationName,
                    CollectionName = instance.CollectionName,
                    Types = [genericParam?.Name],
                    CollectionType = registeredCollection.Key,
                    Registration = Registration.Static,
                    DefinedIndices = instance.BuildIndexMetas().ToArray(),
                };
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

                var colType = _mongoDbInstance.RegisteredCollections.FirstOrDefault(x => x.Key.Name == registeredCollection.Key.Name).Key;
                var collection = _collectionProvider.GetCollection(colType, new DatabaseContext()) as RepositoryCollectionBase;

                if (genericParam?.Name != null)
                {
                    yield return new DynColInfo
                    {
                        Source = Source.Registration,
                        Type = genericParam.Name,
                        CollectionType = registeredCollection.Key,
                        DefinedIndices = collection.BuildIndexMetas().ToArray(),
                    };
                }
                else
                {
                    Debugger.Break();
                }
            }
        }
    }

    internal abstract record ColInfo
    {
        public required Source Source { get; init; }
        public required Type CollectionType { get; init; }
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
