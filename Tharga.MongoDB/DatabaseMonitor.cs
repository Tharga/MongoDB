using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
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
    private readonly ICollectionCache _cache;
    private readonly IQueueMonitor _queueMonitor;
    private Dictionary<(string, string), StatColInfo> _staticLookup;
    private Dictionary<string, DynColInfo> _dynamicLookup;
    private readonly SemaphoreSlim _lookupLock = new(1, 1);
    private bool _started;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, MonitorClientDto> _monitorClients = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CollectionInfo> _remoteCollections = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Collections.Concurrent.ConcurrentDictionary<string, bool>> _collectionSources = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _sourceToConnectionId = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, RemoteQueueState> _remoteQueueStates = new();

    public event EventHandler<CollectionInfoChangedEventArgs> CollectionInfoChangedEvent;
    public event EventHandler<CollectionDroppedEventArgs> CollectionDroppedEvent;
    public event EventHandler MonitorClientsChanged;

    public DatabaseMonitor(IMongoDbServiceFactory mongoDbServiceFactory, IMongoDbInstance mongoDbInstance, IServiceProvider serviceProvider, IRepositoryConfiguration repositoryConfiguration, ICollectionProvider collectionProvider, ICallLibrary callLibrary, ICollectionCache cache, IQueueMonitor queueMonitor, IOptions<DatabaseOptions> options, ILogger<DatabaseMonitor> logger)
    {
        _mongoDbServiceFactory = mongoDbServiceFactory;
        _mongoDbInstance = mongoDbInstance;
        _serviceProvider = serviceProvider;
        _repositoryConfiguration = repositoryConfiguration;
        _collectionProvider = collectionProvider;
        _callLibrary = callLibrary;
        _cache = cache;
        _queueMonitor = queueMonitor;
        _logger = logger;
        _options = options.Value;
    }

    internal void Start(IServiceProvider serviceProvider)
    {
        if (_started) throw new InvalidOperationException($"{nameof(DatabaseMonitor)} has already been started.");

        if (_options.ReadyCallback != null)
        {
            var cacheLoaded = 0;
            _options.ReadyCallback(serviceProvider, async () =>
            {
                if (Interlocked.CompareExchange(ref cacheLoaded, 1, 0) != 0) return;
                try
                {
                    await _cache.LoadAsync();
                    _logger.LogInformation("DatabaseMonitor cache loaded via ReadyCallback.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load DatabaseMonitor cache via ReadyCallback.");
                }
            });
        }
        else
        {
            _cache.LoadAsync().GetAwaiter().GetResult();
        }

        try
        {
            _mongoDbServiceFactory.CollectionAccessEvent += (_, e) =>
            {
                try
                {
                    _logger.LogTrace($"{nameof(IMongoDbServiceFactory.CollectionAccessEvent)}: {e.Fingerprint}");

                    var entry = BuildInitialEntry(e.Fingerprint, e.Server, e.DatabasePart, e.EntityType.Name);

                    // Track previous registration so we can detect reclassification
                    var previousRegistration = _cache.TryGet(e.Fingerprint.Key, out var prev) ? (Registration?)prev.Registration : null;

                    _cache.AddOrUpdate(e.Fingerprint.Key,
                        addFactory: _ => entry with
                        {
                            EntityTypes = entry.EntityTypes.Union([e.EntityType.Name]).ToArray()
                        },
                        updateFactory: (_, existing) =>
                        {
                            // If previously unclassified, use the freshly-built entry as base
                            // (it may now be correctly classified as Dynamic via entityTypeName)
                            if (existing.Registration == Registration.NotInCode)
                            {
                                return entry with
                                {
                                    EntityTypes = entry.EntityTypes.Union([e.EntityType.Name]).ToArray(),
                                    DatabasePart = entry.DatabasePart ?? e.DatabasePart.NullIfEmpty(),
                                    Stats = existing.Stats,
                                    Index = entry.Index != null
                                        ? new IndexInfo { Current = existing.Index?.Current, Defined = entry.Index.Defined, UpdatedAt = existing.Index?.UpdatedAt }
                                        : existing.Index,
                                    Clean = existing.Clean,
                                };
                            }
                            return existing with
                            {
                                EntityTypes = existing.EntityTypes.Union([e.EntityType.Name]).ToArray(),
                                DatabasePart = existing.DatabasePart ?? e.DatabasePart.NullIfEmpty(),
                            };
                        });

                    if (_cache.TryGet(e.Fingerprint.Key, out var item))
                    {
                        if (CollectionInfoChangedEvent != null)
                            CollectionInfoChangedEvent.Invoke(this, new CollectionInfoChangedEventArgs(item));

                        // Persist when a Dynamic collection is first seen or reclassified from NotInCode
                        if (item.Registration == Registration.Dynamic && previousRegistration != Registration.Dynamic)
                            Task.Run(async () => await _cache.SaveAsync(item));
                    }
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

                    Task.Run(async () =>
                    {
                        try
                        {
                            var mongoDbService = GetMongoDbService(e.Fingerprint);
                            var meta = await mongoDbService
                                .GetCollectionsWithMetaAsync(e.Fingerprint.DatabaseName, collectionNameFilter: e.Fingerprint.CollectionName, includeDetails: true)
                                .FirstOrDefaultAsync();

                            if (meta == null) return;

                            _cache.AddOrUpdate(e.Fingerprint.Key,
                                addFactory: _ =>
                                {
                                    _cache.TryGet(e.Fingerprint.Key, out var prev);
                                    var entityTypeName = prev?.EntityTypes?.FirstOrDefault();
                                    var entry = BuildInitialEntry(e.Fingerprint, meta.Server, prev?.DatabasePart, entityTypeName);
                                    return entry with { Index = BuildIndexInfo(entry, meta.Indexes) };
                                },
                                updateFactory: (_, existing) =>
                                    existing with { Index = BuildIndexInfo(existing, meta.Indexes) });

                            if (CollectionInfoChangedEvent != null && _cache.TryGet(e.Fingerprint.Key, out var item))
                            {
                                CollectionInfoChangedEvent?.Invoke(this, new CollectionInfoChangedEventArgs(item));
                                await _cache.SaveAsync(item);
                            }
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
                var keysToRemove = _cache.GetAll()
                    .Where(v => (v.ConfigurationName?.Value ?? _options.DefaultConfigurationName) == configName
                                && v.CollectionName == e.DatabaseContext.CollectionName)
                    .Select(v => v.Key)
                    .ToList();

                var removedEntries = new List<CollectionInfo>();
                foreach (var key in keysToRemove)
                    if (_cache.TryRemove(key, out var removed))
                        removedEntries.Add(removed);

                if (removedEntries.Count > 0)
                    Task.Run(async () =>
                    {
                        foreach (var removed in removedEntries)
                            await _cache.DeleteAsync(removed.DatabaseName, removed.CollectionName);
                    });

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

            _mongoDbServiceFactory.CallEndEvent += (_, e) =>
            {
                try
                {
                    _logger.LogTrace($"{nameof(IMongoDbServiceFactory.CallEndEvent)}: {e.Elapsed}");
                    _callLibrary.EndCall(e);
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

        if (_cache.TryGet(fingerprint.Key, out var cached))
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
        var cachedContexts = _cache.GetAll().Select(x => new DatabaseContext
        {
            ConfigurationName = x.ConfigurationName?.Value ?? _options.DefaultConfigurationName,
            DatabasePart = x.DatabasePart.NullIfEmpty()
        }).Where(x => configuredContexts.Any(y => y.ConfigurationName == x.ConfigurationName));

        // Derive persisted contexts from the cache (pre-loaded from DB storage on startup).
        // This includes tenant databases that had dynamic collections in a previous session.
        var persistedContexts = _cache.GetAll()
            .Where(r => !string.IsNullOrEmpty(r.DatabasePart) && r.Registration != Registration.NotInCode)
            .Select(r => new DatabaseContext
            {
                ConfigurationName = r.ConfigurationName?.Value ?? _options.DefaultConfigurationName,
                DatabasePart = r.DatabasePart
            })
            .ToList();

        var contexts = configuredContexts.Union(cachedContexts).Union(persistedContexts).Distinct().ToArray();

        var sw = new Stopwatch();
        sw.Start();

        var currentDbKeys = new HashSet<string>();
        var visited = new HashSet<string>();
        var index = 0;
        var total = contexts.Length;
        var localSource = _mongoDbServiceFactory.SourceName;

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
                    {
                        var sources = _collectionSources.GetOrAdd(info.Key, _ => new System.Collections.Concurrent.ConcurrentDictionary<string, bool>());
                        sources[localSource] = true;
                        yield return info;
                    }
                }
            }
            else
            {
                await foreach (var info in GetCollectionsFromDb(mongoDbService, null, filter, currentDbKeys, visited, sw))
                {
                    var sources = _collectionSources.GetOrAdd(info.Key, _ => new System.Collections.Concurrent.ConcurrentDictionary<string, bool>());
                    sources[localSource] = true;
                    yield return info;
                }
            }
        }

        // Remove stale cache entries for collections no longer in DB
        foreach (var key in _cache.GetKeys().Where(k => !currentDbKeys.Contains(k)).ToList())
            _cache.TryRemove(key, out _);

        // Append remote collections not already present locally
        foreach (var remote in _remoteCollections.Values)
        {
            if (!currentDbKeys.Contains(remote.Key))
            {
                yield return remote;
            }
        }
    }

    public async Task RefreshStatsAsync(CollectionFingerprint fingerprint)
    {
        if (!_started) throw new InvalidOperationException($"{nameof(DatabaseMonitor)} has not been started. Call {nameof(MongoDbRegistrationExtensions.UseMongoDB)} on application start.");
        if (fingerprint == null) throw new ArgumentNullException(nameof(fingerprint));

        var mongoDbService = GetMongoDbService(fingerprint);
        var meta = await mongoDbService
            .GetCollectionsWithMetaAsync(fingerprint.DatabaseName, collectionNameFilter: fingerprint.CollectionName, includeDetails: true)
            .FirstOrDefaultAsync();
        if (meta == null) return;

        var now = DateTime.UtcNow;

        // Use cached entry to restore entity type name for dynamic collections
        _cache.TryGet(fingerprint.Key, out var cachedEntry);

        var updated = _cache.AddOrUpdate(fingerprint.Key,
            addFactory: _ =>
            {
                // Use stored entity type name so dynamic collections are correctly recognised when not in cache
                var entityTypeName = cachedEntry != null && cachedEntry.Registration != Registration.NotInCode
                    ? cachedEntry.EntityTypes?.FirstOrDefault()
                    : null;
                var entry = BuildInitialEntry(fingerprint, meta.Server, cachedEntry?.DatabasePart, entityTypeName);
                return entry with
                {
                    Stats = new CollectionStats { DocumentCount = meta.DocumentCount, Size = meta.Size, UpdatedAt = now },
                    Index = BuildIndexInfo(entry, meta.Indexes, now),
                    Clean = cachedEntry?.Clean,
                };
            },
            updateFactory: (_, existing) => existing with
            {
                Stats = new CollectionStats { DocumentCount = meta.DocumentCount, Size = meta.Size, UpdatedAt = now },
                Index = BuildIndexInfo(existing, meta.Indexes, now),
                CurrentSchemaFingerprint = existing.CurrentSchemaFingerprint ?? ComputeSchemaFingerprint(existing.CollectionType),
            });

        CollectionInfoChangedEvent?.Invoke(this, new CollectionInfoChangedEventArgs(updated));
        _ = Task.Run(() => _cache.SaveAsync(updated));
    }

    public async Task TouchAsync(CollectionInfo collectionInfo)
    {
        if (!_started) throw new InvalidOperationException($"{nameof(DatabaseMonitor)} has not been started. Call {nameof(MongoDbRegistrationExtensions.UseMongoDB)} on application start.");
        if (collectionInfo == null) throw new ArgumentNullException(nameof(collectionInfo));

        if (collectionInfo.CollectionType == null)
        {
            var dispatcher = TryGetRemoteDispatcher(collectionInfo);
            if (dispatcher.Dispatcher != null)
            {
                await dispatcher.Dispatcher.TouchAsync(dispatcher.ConnectionId, collectionInfo);
                return;
            }
            throw new InvalidOperationException("Collection is remote-only but no connected agent was found.");
        }

        if (collectionInfo.Registration == Registration.NotInCode) throw new InvalidOperationException($"{nameof(TouchAsync)} does not support {nameof(Registration)} {collectionInfo.Registration}.");

        var collection = _collectionProvider.GetCollection(collectionInfo.CollectionType, collectionInfo.Registration == Registration.Dynamic ? collectionInfo.ToDatabaseContext() : null);

        _ = await FetchMongoCollection(collection.GetType(), collection, true);

        var mongoDbService = GetMongoDbService(collectionInfo);
        var meta = await mongoDbService
            .GetCollectionsWithMetaAsync(collectionInfo.DatabaseName, collectionNameFilter: collectionInfo.CollectionName, includeDetails: true)
            .FirstOrDefaultAsync();

        if (meta == null) return;

        var now = DateTime.UtcNow;
        var updated = _cache.AddOrUpdate(collectionInfo.Key,
            addFactory: _ => collectionInfo with
            {
                Stats = new CollectionStats { DocumentCount = meta.DocumentCount, Size = meta.Size, UpdatedAt = now },
                Index = BuildIndexInfo(collectionInfo, meta.Indexes, now)
            },
            updateFactory: (_, existing) => existing with
            {
                Stats = new CollectionStats { DocumentCount = meta.DocumentCount, Size = meta.Size, UpdatedAt = now },
                Index = BuildIndexInfo(existing, meta.Indexes, now)
            });

        CollectionInfoChangedEvent?.Invoke(this, new CollectionInfoChangedEventArgs(updated));
        _ = Task.Run(() => _cache.SaveAsync(updated));
    }

    public async Task<(int Before, int After)> DropIndexAsync(CollectionInfo collectionInfo)
    {
        if (!_started) throw new InvalidOperationException($"{nameof(DatabaseMonitor)} has not been started. Call {nameof(MongoDbRegistrationExtensions.UseMongoDB)} on application start.");
        if (collectionInfo == null) throw new ArgumentNullException(nameof(collectionInfo));

        if (collectionInfo.CollectionType == null)
        {
            var dispatcher = TryGetRemoteDispatcher(collectionInfo);
            if (dispatcher.Dispatcher != null)
                return await dispatcher.Dispatcher.DropIndexAsync(dispatcher.ConnectionId, collectionInfo);
            throw new InvalidOperationException("Collection is remote-only but no connected agent was found.");
        }

        if (collectionInfo.Registration == Registration.NotInCode) throw new InvalidOperationException($"{nameof(DropIndexAsync)} does not support {nameof(Registration)} {collectionInfo.Registration}.");

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

    public async Task RestoreIndexAsync(CollectionInfo collectionInfo, bool force)
    {
        if (!_started) throw new InvalidOperationException($"{nameof(DatabaseMonitor)} has not been started. Call {nameof(MongoDbRegistrationExtensions.UseMongoDB)} on application start.");
        if (collectionInfo == null) throw new ArgumentNullException(nameof(collectionInfo));

        if (collectionInfo.CollectionType == null)
        {
            var dispatcher = TryGetRemoteDispatcher(collectionInfo);
            if (dispatcher.Dispatcher != null)
            {
                await dispatcher.Dispatcher.RestoreIndexAsync(dispatcher.ConnectionId, collectionInfo, force);
                return;
            }
            throw new InvalidOperationException("Collection is remote-only but no connected agent was found.");
        }

        if (collectionInfo.Registration == Registration.NotInCode) throw new InvalidOperationException($"{nameof(RestoreIndexAsync)} does not support {nameof(Registration)} {collectionInfo.Registration}.");

        var collection = _collectionProvider.GetCollection(collectionInfo.CollectionType, collectionInfo.Registration == Registration.Dynamic ? collectionInfo.ToDatabaseContext() : null);

        var ct = collection.GetType();
        var mongoCollection = await FetchMongoCollection(ct, collection, true);

        var restoreMethod = ct.GetMethod(nameof(DiskRepositoryCollectionBase<EntityBase>.AssureIndex), BindingFlags.Instance | BindingFlags.NonPublic);
        var restoreResult = restoreMethod?.Invoke(collection, [mongoCollection, force, true]);
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

        if (collectionInfo.CollectionType == null)
        {
            var dispatcher = TryGetRemoteDispatcher(collectionInfo);
            if (dispatcher.Dispatcher != null)
                return await dispatcher.Dispatcher.CleanAsync(dispatcher.ConnectionId, collectionInfo, cleanGuids);
            throw new InvalidOperationException("Collection is remote-only but no connected agent was found.");
        }

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

        var updated = _cache.AddOrUpdate(collectionInfo.Key,
            addFactory: _ => collectionInfo with { Clean = result },
            updateFactory: (_, existing) => existing with { Clean = result });

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

    public void IngestCall(CallDto call)
    {
        _callLibrary.IngestCall(FromCallDto(call));
    }

    public void ResetCalls()
    {
        _callLibrary.ResetCalls();
    }

    // --- Remote client management ---

    public IEnumerable<MonitorClientDto> GetMonitorClients()
    {
        return _monitorClients.Values;
    }

    public void IngestClientConnected(MonitorClientDto client)
    {
        _monitorClients[client.Instance] = client;
        MonitorClientsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void IngestClientDisconnected(string connectionId)
    {
        var entry = _monitorClients.Values.FirstOrDefault(x => x.ConnectionId == connectionId);
        if (entry != null)
        {
            _monitorClients[entry.Instance] = entry with { IsConnected = false, DisconnectTime = DateTime.UtcNow };
            MonitorClientsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void IngestCollectionInfo(RemoteCollectionInfoDto dto, string connectionId = null)
    {
        Enum.TryParse<Discovery>(dto.Discovery, out var discovery);
        Enum.TryParse<Registration>(dto.Registration, out var registration);
        var info = new CollectionInfo
        {
            ConfigurationName = dto.ConfigurationName,
            DatabaseName = dto.DatabaseName,
            CollectionName = dto.CollectionName,
            Server = dto.Server,
            DatabasePart = dto.DatabasePart,
            Discovery = discovery,
            Registration = registration,
            EntityTypes = dto.EntityTypes ?? [],
            CollectionType = null,
            Stats = dto.Stats,
            Index = dto.Index,
            Clean = dto.Clean,
        };

        var key = info.Key;
        _remoteCollections[key] = info;

        // Track source
        var sources = _collectionSources.GetOrAdd(key, _ => new System.Collections.Concurrent.ConcurrentDictionary<string, bool>());
        sources[dto.SourceName] = true;

        // Map source to connection for action delegation
        if (!string.IsNullOrEmpty(connectionId) && !string.IsNullOrEmpty(dto.SourceName))
        {
            _sourceToConnectionId[dto.SourceName] = connectionId;

            // Update client SourceName if known
            var client = _monitorClients.Values.FirstOrDefault(x => x.ConnectionId == connectionId);
            if (client != null)
                _monitorClients[client.Instance] = client with { SourceName = dto.SourceName };
        }

        CollectionInfoChangedEvent?.Invoke(this, new CollectionInfoChangedEventArgs(info));
    }

    public IReadOnlyCollection<string> GetCollectionSources(string fingerprintKey)
    {
        if (_collectionSources.TryGetValue(fingerprintKey, out var sources))
            return sources.Keys.ToArray();
        return [];
    }

    public string FindConnectionIdBySource(string sourceName)
    {
        if (string.IsNullOrEmpty(sourceName)) return null;
        if (!_sourceToConnectionId.TryGetValue(sourceName, out var connectionId)) return null;

        // Verify the client is still connected
        var client = _monitorClients.Values.FirstOrDefault(x => x.ConnectionId == connectionId && x.IsConnected);
        return client != null ? connectionId : null;
    }

    public void IngestQueueMetric(string sourceName, int queueCount, int executingCount, double? waitTimeMs)
    {
        _remoteQueueStates[sourceName] = new RemoteQueueState
        {
            QueueCount = queueCount,
            ExecutingCount = executingCount,
            LastWaitTimeMs = waitTimeMs ?? 0,
            Timestamp = DateTime.UtcNow,
        };
    }

    public IReadOnlyDictionary<string, ConnectionPoolStateDto> GetPerSourceQueueState()
    {
        var result = new Dictionary<string, ConnectionPoolStateDto>();

        // Local
        var localSource = _mongoDbServiceFactory.SourceName;
        result[localSource] = GetConnectionPoolState();

        // Remote
        foreach (var (source, state) in _remoteQueueStates)
        {
            result[source] = new ConnectionPoolStateDto
            {
                QueueCount = state.QueueCount,
                ExecutingCount = state.ExecutingCount,
                LastWaitTimeMs = state.LastWaitTimeMs,
                RecentMetrics = [],
            };
        }

        return result;
    }

    private record RemoteQueueState
    {
        public int QueueCount { get; init; }
        public int ExecutingCount { get; init; }
        public double LastWaitTimeMs { get; init; }
        public DateTime Timestamp { get; init; }
    }

    // --- API-friendly methods ---

    public IEnumerable<CallDto> GetCallDtos(CallType callType)
    {
        return GetCalls(callType).Select(ToCallDto);
    }

    public async Task<string> GetExplainAsync(Guid callKey, CancellationToken cancellationToken = default)
    {
        if (!_started) throw new InvalidOperationException($"{nameof(DatabaseMonitor)} has not been started. Call {nameof(MongoDbRegistrationExtensions.UseMongoDB)} on application start.");

        var call = _callLibrary.GetCall(callKey);
        if (call?.ExplainProvider == null) return null;
        return await call.ExplainProvider(cancellationToken);
    }

    public IReadOnlyDictionary<string, int> GetCallCounts()
    {
        if (!_started) throw new InvalidOperationException($"{nameof(DatabaseMonitor)} has not been started. Call {nameof(MongoDbRegistrationExtensions.UseMongoDB)} on application start.");

        return _callLibrary.GetCallCounts();
    }

    public IEnumerable<CallSummaryDto> GetCallSummary()
    {
        if (!_started) throw new InvalidOperationException($"{nameof(DatabaseMonitor)} has not been started. Call {nameof(MongoDbRegistrationExtensions.UseMongoDB)} on application start.");

        return _callLibrary.GetLastCalls()
            .Where(c => c.Elapsed.HasValue)
            .GroupBy(c => (c.SourceName, c.Fingerprint.ConfigurationName.Value, c.Fingerprint.DatabaseName, c.Fingerprint.CollectionName, c.FunctionName))
            .Select(g =>
            {
                var elapsed = g.Select(c => c.Elapsed.Value.TotalMilliseconds).ToArray();
                return new CallSummaryDto
                {
                    SourceName = g.Key.SourceName,
                    ConfigurationName = g.Key.Value,
                    DatabaseName = g.Key.DatabaseName,
                    CollectionName = g.Key.CollectionName,
                    FunctionName = g.Key.FunctionName,
                    CallCount = elapsed.Length,
                    AvgElapsedMs = elapsed.Average(),
                    MaxElapsedMs = elapsed.Max(),
                    MinElapsedMs = elapsed.Min(),
                    TotalElapsedMs = elapsed.Sum()
                };
            })
            .OrderByDescending(x => x.TotalElapsedMs);
    }

    public IEnumerable<ErrorSummaryDto> GetErrorSummary()
    {
        if (!_started) throw new InvalidOperationException($"{nameof(DatabaseMonitor)} has not been started. Call {nameof(MongoDbRegistrationExtensions.UseMongoDB)} on application start.");

        return _callLibrary.GetLastCalls()
            .Where(c => c.Exception != null)
            .GroupBy(c => (c.SourceName, c.Fingerprint.ConfigurationName.Value, c.Fingerprint.DatabaseName, c.Fingerprint.CollectionName, ExceptionType: c.Exception.GetType().Name))
            .Select(g => new ErrorSummaryDto
            {
                SourceName = g.Key.SourceName,
                ConfigurationName = g.Key.Value,
                DatabaseName = g.Key.DatabaseName,
                CollectionName = g.Key.CollectionName,
                ExceptionType = g.Key.ExceptionType,
                Message = g.First().Exception.Message,
                Count = g.Count(),
                LastOccurrence = g.Max(c => c.StartTime)
            })
            .OrderByDescending(x => x.Count);
    }

    public async IAsyncEnumerable<SlowCallWithIndexInfoDto> GetSlowCallsWithIndexInfoAsync()
    {
        if (!_started) throw new InvalidOperationException($"{nameof(DatabaseMonitor)} has not been started. Call {nameof(MongoDbRegistrationExtensions.UseMongoDB)} on application start.");

        var slowCalls = _callLibrary.GetSlowCalls().ToArray();

        foreach (var call in slowCalls)
        {
            string[] definedIndexNames = [];
            var hasCoverage = false;

            try
            {
                var instance = await GetInstanceAsync(call.Fingerprint);
                if (instance?.Index?.Defined != null)
                {
                    definedIndexNames = instance.Index.Defined.Select(x => x.Name).ToArray();
                    hasCoverage = definedIndexNames.Length > 0;
                }
            }
            catch
            {
                // Collection may no longer exist
            }

            yield return new SlowCallWithIndexInfoDto
            {
                Call = ToCallDto(call),
                DefinedIndexNames = definedIndexNames,
                HasPotentialIndexCoverage = hasCoverage
            };
        }
    }

    public ConnectionPoolStateDto GetConnectionPoolState()
    {
        var (queueCount, executingCount, lastWaitTimeMs) = _queueMonitor.GetCurrentState();
        var recentMetrics = _queueMonitor.GetRecentMetrics()
            .Select(m => new QueueMetricDto
            {
                Timestamp = m.Timestamp,
                QueueCount = m.QueueCount,
                ExecutingCount = m.ExecutingCount,
                WaitTimeMs = m.WaitTime?.TotalMilliseconds
            })
            .ToArray();

        return new ConnectionPoolStateDto
        {
            QueueCount = queueCount,
            ExecutingCount = executingCount,
            LastWaitTimeMs = lastWaitTimeMs,
            RecentMetrics = recentMetrics
        };
    }

    private static CallInfo FromCallDto(CallDto dto)
    {
        Enum.TryParse<Operation>(dto.Operation, out var operation);
        return new CallInfo
        {
            Key = dto.Key,
            StartTime = dto.StartTime,
            SourceName = dto.SourceName,
            Fingerprint = new CollectionFingerprint
            {
                ConfigurationName = dto.ConfigurationName,
                DatabaseName = dto.DatabaseName,
                CollectionName = dto.CollectionName,
            },
            FunctionName = dto.FunctionName,
            Operation = operation,
            Elapsed = dto.ElapsedMs.HasValue ? TimeSpan.FromMilliseconds(dto.ElapsedMs.Value) : null,
            Count = dto.Count,
            Final = dto.Final,
            Steps = dto.Steps?.Select(s => new CallStepInfo
            {
                Step = s.Step,
                Delta = TimeSpan.FromMilliseconds(s.DeltaMs),
                Message = s.Message
            }).ToArray()
        };
    }

    private static CallDto ToCallDto(CallInfo call)
    {
        return new CallDto
        {
            Key = call.Key,
            StartTime = call.StartTime,
            SourceName = call.SourceName,
            ConfigurationName = call.Fingerprint.ConfigurationName.Value,
            DatabaseName = call.Fingerprint.DatabaseName,
            CollectionName = call.Fingerprint.CollectionName,
            FunctionName = call.FunctionName,
            Operation = call.Operation.ToString(),
            ElapsedMs = call.Elapsed?.TotalMilliseconds,
            Count = call.Count,
            Exception = call.Exception?.Message,
            Final = call.Final,
            FilterJson = call.FilterJson,
            Steps = call.Steps?.Select(s => new CallStepDto
            {
                Step = s.Step,
                DeltaMs = s.Delta.TotalMilliseconds,
                Message = s.Message
            }).ToArray()
        };
    }

    // --- Private helpers ---

    private (IRemoteActionDispatcher Dispatcher, string ConnectionId) TryGetRemoteDispatcher(CollectionInfo collectionInfo)
    {
        var dispatcher = _serviceProvider.GetService(typeof(IRemoteActionDispatcher)) as IRemoteActionDispatcher;
        if (dispatcher == null) return (null, null);

        var sources = GetCollectionSources(collectionInfo.Key);
        foreach (var source in sources)
        {
            var connectionId = FindConnectionIdBySource(source);
            if (connectionId != null) return (dispatcher, connectionId);
        }

        return (null, null);
    }

    private async Task<CollectionInfo> LoadAndCacheAsync(CollectionFingerprint fingerprint)
    {
        var mongoDbService = GetMongoDbService(fingerprint);
        var meta = await mongoDbService
            .GetCollectionsWithMetaAsync(fingerprint.DatabaseName, collectionNameFilter: fingerprint.CollectionName, includeDetails: true)
            .FirstOrDefaultAsync();
        if (meta == null) return null;

        // Use cached entry (pre-loaded from DB storage) to restore entity type name for Dynamic collections
        _cache.TryGet(fingerprint.Key, out var cachedEntry);
        var entityTypeName = cachedEntry != null && cachedEntry.Registration != Registration.NotInCode
            ? cachedEntry.EntityTypes?.FirstOrDefault()
            : null;

        var now = DateTime.UtcNow;
        var entry = BuildInitialEntry(fingerprint, meta.Server, cachedEntry?.DatabasePart, entityTypeName);
        entry = entry with
        {
            Stats = new CollectionStats { DocumentCount = meta.DocumentCount, Size = meta.Size, UpdatedAt = now },
            Index = BuildIndexInfo(entry, meta.Indexes, now),
            Clean = cachedEntry?.Clean,
        };
        _cache.Set(fingerprint.Key, entry);
        _ = Task.Run(() => _cache.SaveAsync(entry));
        return entry;
    }

    private CollectionInfo BuildInitialEntry(CollectionFingerprint fingerprint, string server, string databasePart, string entityTypeName)
    {
        var (staticLookup, dynamicLookup) = GetLookups();
        var configName = fingerprint.ConfigurationName?.Value ?? _options.DefaultConfigurationName;

        if (staticLookup.TryGetValue((configName, fingerprint.CollectionName), out var reg))
        {
            return new CollectionInfo
            {
                ConfigurationName = fingerprint.ConfigurationName,
                DatabaseName = fingerprint.DatabaseName,
                CollectionName = fingerprint.CollectionName,
                Server = server ?? string.Empty,
                DatabasePart = databasePart.NullIfEmpty(),
                Discovery = reg.Discovery | Discovery.Database,
                Registration = reg.Registration,
                EntityTypes = reg.EntityTypes,
                CollectionType = reg.CollectionType,
                Index = new IndexInfo { Current = null, Defined = reg.DefinedIndices },
                CurrentSchemaFingerprint = reg.EntityType != null ? SchemaFingerprint.Generate(reg.EntityType) : null,
            };
        }

        if (entityTypeName != null && dynamicLookup.TryGetValue(entityTypeName, out var dyn))
        {
            return new CollectionInfo
            {
                ConfigurationName = fingerprint.ConfigurationName,
                DatabaseName = fingerprint.DatabaseName,
                CollectionName = fingerprint.CollectionName,
                Server = server ?? string.Empty,
                DatabasePart = databasePart.NullIfEmpty(),
                Discovery = dyn.Discovery | Discovery.Database,
                Registration = Registration.Dynamic,
                EntityTypes = [entityTypeName],
                CollectionType = dyn.CollectionType,
                Index = new IndexInfo { Current = null, Defined = dyn.DefinedIndices },
                CurrentSchemaFingerprint = dyn.EntityType != null ? SchemaFingerprint.Generate(dyn.EntityType) : null,
            };
        }

        return new CollectionInfo
        {
            ConfigurationName = fingerprint.ConfigurationName,
            DatabaseName = fingerprint.DatabaseName,
            CollectionName = fingerprint.CollectionName,
            Server = server ?? string.Empty,
            DatabasePart = databasePart.NullIfEmpty(),
            Discovery = Discovery.Database,
            Registration = Registration.NotInCode,
            EntityTypes = entityTypeName != null ? [entityTypeName] : [],
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
            var a = GetDynamicRegistrations(_staticLookup.Select(x => new DatabaseContext { ConfigurationName = x.Key.Item1 })).ToArray();
            var b = a.GroupBy(x => x.Type).ToArray();
            var c = b.Where(x => x.Count() > 1).ToArray();
            var d = b.Select(x => x.First());
            _dynamicLookup = d.ToDictionary(x => x.Type, x => x);

            return (_staticLookup, _dynamicLookup);
        }
        finally
        {
            _lookupLock.Release();
        }
    }

    public async Task ResetAsync()
    {
        if (!_started) throw new InvalidOperationException($"{nameof(DatabaseMonitor)} has not been started. Call {nameof(MongoDbRegistrationExtensions.UseMongoDB)} on application start.");
        await _cache.ResetAsync();
    }

    private static string ComputeSchemaFingerprint(Type collectionType)
    {
        if (collectionType == null) return null;
        var entityType = ResolveEntityType(collectionType);
        return entityType != null ? SchemaFingerprint.Generate(entityType) : null;
    }

    private static IndexInfo BuildIndexInfo(CollectionInfo existing, IndexMeta[] currentIndexes, DateTime? updatedAt = null)
    {
        var defined = existing.Index?.Defined ?? [];
        return new IndexInfo { Current = currentIndexes, Defined = defined, UpdatedAt = updatedAt ?? existing.Index?.UpdatedAt };
    }

    private async Task UpdateIndexCacheAsync(CollectionInfo collectionInfo)
    {
        var mongoDbService = GetMongoDbService(collectionInfo);
        var meta = await mongoDbService
            .GetCollectionsWithMetaAsync(collectionInfo.DatabaseName, collectionNameFilter: collectionInfo.CollectionName, includeDetails: true)
            .FirstOrDefaultAsync();

        if (meta == null) return;

        var updated = _cache.AddOrUpdate(collectionInfo.Key,
            addFactory: _ => collectionInfo with { Index = BuildIndexInfo(collectionInfo, meta.Indexes, DateTime.UtcNow) },
            updateFactory: (_, existing) => existing with { Index = BuildIndexInfo(existing, meta.Indexes, DateTime.UtcNow) });

        CollectionInfoChangedEvent?.Invoke(this, new CollectionInfoChangedEventArgs(updated));
        _ = Task.Run(() => _cache.SaveAsync(updated));
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
        var cleanInfos = await mongoDbService.ReadAllCleanInfoAsync(databaseName);

        await foreach (var meta in mongoDbService.GetCollectionsWithMetaAsync(databaseName, includeDetails: false))
        {
            if (meta.CollectionName.StartsWith("_")) continue;
            if (filter != null && !meta.CollectionName.ProtectCollectionName().Contains(filter)) continue;

            var key = $"{meta.ConfigurationName}.{meta.DatabaseName}.{meta.CollectionName}";
            currentDbKeys.Add(key);

            if (!visited.Add(key)) continue;

            Console.WriteLine($"- Got {meta.CollectionName} [{sw.Elapsed.TotalSeconds:N0}s]");

            cleanInfos.TryGetValue(meta.CollectionName, out var cleanInfo);

            if (_cache.TryGet(key, out var cached))
            {
                // Enrich cache-loaded entries with code-derived info (defined indices, schema fingerprint)
                var needsEnrich = cached.CurrentSchemaFingerprint == null
                    || (cached.Index != null && (cached.Index.Defined == null || cached.Index.Defined.Length == 0));
                if (needsEnrich)
                {
                    var fp = new CollectionFingerprint
                    {
                        ConfigurationName = meta.ConfigurationName,
                        DatabaseName = meta.DatabaseName,
                        CollectionName = meta.CollectionName
                    };
                    var codeEntry = BuildInitialEntry(fp, cached.Server, cached.DatabasePart, cached.EntityTypes?.FirstOrDefault());
                    cached = cached with
                    {
                        CurrentSchemaFingerprint = cached.CurrentSchemaFingerprint ?? codeEntry.CurrentSchemaFingerprint,
                        Index = cached.Index != null
                            ? new IndexInfo { Current = cached.Index.Current, Defined = codeEntry.Index?.Defined ?? cached.Index.Defined, UpdatedAt = cached.Index.UpdatedAt }
                            : codeEntry.Index,
                        Registration = codeEntry.Registration != Registration.NotInCode ? codeEntry.Registration : cached.Registration,
                        Discovery = cached.Discovery | codeEntry.Discovery,
                    };
                }

                // Always refresh CleanInfo from _clean (single source of truth)
                cached = cached with { Clean = cleanInfo };
                _cache.Set(key, cached);
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
                // Try to recover entity type from persisted cache so dynamic collections keep their defined indices
                _cache.TryGet(key, out var persisted);
                var entityTypeName = persisted?.EntityTypes?.FirstOrDefault();
                var entry = BuildInitialEntry(fp, meta.Server, persisted?.DatabasePart, entityTypeName) with { Clean = cleanInfo };
                _cache.Set(key, entry);
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
                    Discovery = Discovery.Registration,
                    ConfigurationName = instance.ConfigurationName,
                    CollectionName = instance.CollectionName,
                    EntityTypes = [genericParam?.Name],
                    CollectionType = registeredCollection.Key,
                    Registration = Registration.Static,
                    DefinedIndices = instance.BuildIndexMetas().ToArray(),
                    EntityType = genericParam,
                };
            }
        }
    }

    private IEnumerable<DynColInfo> GetDynamicRegistrations(IEnumerable<DatabaseContext> databaseContexts)
    {
        var ctx = databaseContexts.DistinctBy(x => x.ConfigurationName).ToArray();

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
                foreach (var databaseContext in ctx)
                {
                    var collection = _collectionProvider.GetCollection(colType, databaseContext) as RepositoryCollectionBase;

                    if (genericParam?.Name != null)
                    {
                        yield return new DynColInfo
                        {
                            Discovery = Discovery.Registration,
                            Type = genericParam.Name,
                            CollectionType = registeredCollection.Key,
                            DefinedIndices = collection.BuildIndexMetas().ToArray(),
                            EntityType = genericParam,
                        };
                    }
                    else
                    {
                        Debugger.Break();
                    }
                }
            }
        }
    }

    internal abstract record ColInfo
    {
        public required Discovery Discovery { get; init; }
        public required Type CollectionType { get; init; }
        public required IndexMeta[] DefinedIndices { get; init; }
        public Type EntityType { get; init; }
    }

    internal record StatColInfo : ColInfo
    {
        public required string ConfigurationName { get; init; }
        public required string CollectionName { get; init; }
        public required Registration Registration { get; init; }
        public required string[] EntityTypes { get; init; }
    }

    internal record DynColInfo : ColInfo
    {
        public required string Type { get; init; }
    }
}
