using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
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
    private readonly IConnectionPoolMonitor _connectionPoolMonitor;
    private Dictionary<(string, string), StatColInfo> _staticLookup;
    private Dictionary<string, DynColInfo> _dynamicLookup;
    private Dictionary<(string, string), DynColInfo> _dynamicByNameLookup;
    private readonly SemaphoreSlim _lookupLock = new(1, 1);
    private bool _started;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, MonitorClientDto> _monitorClients = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Collections.Concurrent.ConcurrentDictionary<string, bool>> _collectionSources = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _sourceToConnectionId = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, RemoteQueueState> _remoteQueueStates = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, IReadOnlyList<PoolMetricDto>> _remotePoolStates = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, MonitorClientStatus> _clientStatus = new();

    // Per-process effective source identity, keyed by the connection's Instance GUID (stable across reconnects).
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, string> _instanceEffectiveSource = new();
    private readonly object _effectiveSourceLock = new();

    public event EventHandler<CollectionInfoChangedEventArgs> CollectionInfoChangedEvent;
    public event EventHandler<CollectionDroppedEventArgs> CollectionDroppedEvent;
    public event EventHandler MonitorClientsChanged;

    public DatabaseMonitor(IMongoDbServiceFactory mongoDbServiceFactory, IMongoDbInstance mongoDbInstance, IServiceProvider serviceProvider, IRepositoryConfiguration repositoryConfiguration, ICollectionProvider collectionProvider, ICallLibrary callLibrary, ICollectionCache cache, IQueueMonitor queueMonitor, IConnectionPoolMonitor connectionPoolMonitor, IOptions<DatabaseOptions> options, ILogger<DatabaseMonitor> logger)
    {
        _mongoDbServiceFactory = mongoDbServiceFactory;
        _mongoDbInstance = mongoDbInstance;
        _serviceProvider = serviceProvider;
        _repositoryConfiguration = repositoryConfiguration;
        _collectionProvider = collectionProvider;
        _callLibrary = callLibrary;
        _cache = cache;
        _queueMonitor = queueMonitor;
        _connectionPoolMonitor = connectionPoolMonitor;
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
            // A connectivity failure while loading the persisted cache must not abort process
            // startup — the monitor starts degraded (empty cache, repopulated live on first
            // access). Connectivity-fail-fast, when requested, is enforced earlier by the
            // startup pre-check in UseMongoDB; reaching here means we are starting regardless.
            try
            {
                _cache.LoadAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load DatabaseMonitor cache at startup. Starting monitor with an empty cache; it will repopulate as collections are accessed.");
            }
        }

        try
        {
            _mongoDbServiceFactory.CollectionAccessEvent += (_, e) =>
            {
                try
                {
                    _logger.LogTrace($"{nameof(IMongoDbServiceFactory.CollectionAccessEvent)}: {e.Fingerprint}");

                    // Tag the local source first so it's recorded even if the cache update or
                    // event raise below short-circuits — the access itself is the canonical
                    // "touched by me" signal.
                    TagLocalSource(e.Fingerprint.Key);

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
                        RaiseLocalCollectionInfoChanged(item);

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

                            if (_cache.TryGet(e.Fingerprint.Key, out var item))
                            {
                                RaiseLocalCollectionInfoChanged(item);
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
                // Prefer the resolved identity carried on the event (authoritative, matches how the
                // collection was reported); fall back to the registration-time DatabaseContext for
                // older callers. Matching on DatabaseName too, when known, keeps a per-tenant drop
                // from evicting the same collection in other tenant databases.
                var collectionName = e.CollectionName ?? e.DatabaseContext?.CollectionName;
                var databaseName = e.DatabaseName;
                var configName = e.ConfigurationName
                                 ?? e.DatabaseContext?.ConfigurationName?.Value
                                 ?? _options.DefaultConfigurationName;

                var keysToRemove = _cache.GetAll()
                    .Where(v => (v.ConfigurationName?.Value ?? _options.DefaultConfigurationName) == configName
                                && v.CollectionName == collectionName
                                && (databaseName == null || v.DatabaseName == databaseName))
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
            // Agent-reported entries live on the agent's database, not ours, so never probe or evict
            // them against the local server. Same for entries on a configuration this process doesn't
            // host (probing would throw "Cannot find ConnectionStrings/<name>").
            if (IsRemoteOrigin(cached) || !IsConfigurationLocal(fingerprint.ConfigurationName?.Value))
                return cached;

            var mongoDbService = GetMongoDbService(fingerprint);
            if (await mongoDbService.DoesCollectionExist(fingerprint.CollectionName))
                return cached;

            _cache.TryRemove(fingerprint.Key, out _);
            return null;
        }

        // Not cached and not reachable locally — nothing to load.
        if (!IsConfigurationLocal(fingerprint.ConfigurationName?.Value))
            return null;

        return await LoadAndCacheAsync(fingerprint);
    }

    /// <summary>True when the entry was reported by a remote agent (its data lives on the agent's database, not this server's).</summary>
    private static bool IsRemoteOrigin(CollectionInfo info) => info != null && info.Discovery.HasFlag(Discovery.Remote);

    /// <summary>True when this process has the given configuration registered (so it can open a connection to it).</summary>
    private bool IsConfigurationLocal(string configName)
    {
        var name = configName ?? _options.DefaultConfigurationName;
        return GetConfigurations().Any(c => (c.Value ?? _options.DefaultConfigurationName) == name);
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
            _logger?.LogDebug("Scanning context {Index} of {Total}: {Configuration}.{DatabasePart}.{Collection} [{Elapsed:N0}s]", ++index, total, context.ConfigurationName, context.DatabasePart, context.CollectionName, sw.Elapsed.TotalSeconds);
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

        // Remove stale cache entries for locally-scanned collections no longer in the DB. Remote-reported
        // entries are intentionally skipped — they live on an agent's database, not ours, so the local
        // scan can never confirm them and evicting here would discard the persisted report.
        foreach (var key in _cache.GetKeys().Where(k => !currentDbKeys.Contains(k)).ToList())
        {
            if (_cache.TryGet(key, out var entry) && IsRemoteOrigin(entry)) continue;
            _cache.TryRemove(key, out _);
        }

        // Append persisted entries not surfaced by the local scan — chiefly remote-reported collections,
        // including those on configurations this server doesn't host.
        foreach (var cachedInfo in _cache.GetAll())
        {
            if (!currentDbKeys.Contains(cachedInfo.Key))
                yield return cachedInfo;
        }
    }

    public async Task RefreshStatsAsync(CollectionFingerprint fingerprint)
    {
        if (!_started) throw new InvalidOperationException($"{nameof(DatabaseMonitor)} has not been started. Call {nameof(MongoDbRegistrationExtensions.UseMongoDB)} on application start.");
        if (fingerprint == null) throw new ArgumentNullException(nameof(fingerprint));

        // Without the configuration registered here we can't open a connection to refresh; the stats
        // stay as last reported by the agent. When the config *is* local the refresh runs even for a
        // remote-reported collection the server also hosts — GetCollectionsWithMetaAsync simply returns
        // null (handled below) when it isn't present locally, leaving the reported data untouched.
        if (!IsConfigurationLocal(fingerprint.ConfigurationName?.Value))
            return;

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
                    ReportedAt = now,
                };
            },
            updateFactory: (_, existing) => existing with
            {
                Stats = new CollectionStats { DocumentCount = meta.DocumentCount, Size = meta.Size, UpdatedAt = now },
                Index = BuildIndexInfo(existing, meta.Indexes, now),
                CurrentSchemaFingerprint = SchemaFingerprint.IsCurrentVersion(existing.CurrentSchemaFingerprint)
                    ? existing.CurrentSchemaFingerprint
                    : ComputeSchemaFingerprint(existing.CollectionType),
                ReportedAt = now,
            });

        RaiseLocalCollectionInfoChanged(updated);
        _ = Task.Run(() => _cache.SaveAsync(updated));
    }

    public async Task TouchAsync(CollectionInfo collectionInfo)
    {
        if (!_started) throw new InvalidOperationException($"{nameof(DatabaseMonitor)} has not been started. Call {nameof(MongoDbRegistrationExtensions.UseMongoDB)} on application start.");
        if (collectionInfo == null) throw new ArgumentNullException(nameof(collectionInfo));

        var exec = ResolveExecution(collectionInfo);
        if (exec.Target == ExecutionTarget.Remote)
        {
            _logger?.LogDebug("TouchAsync: Delegating to agent via connection {ConnectionId}", exec.ConnectionId);
            await exec.Dispatcher.TouchAsync(exec.ConnectionId, collectionInfo);
            _logger?.LogDebug("TouchAsync: Remote delegation completed.");
            return;
        }
        if (exec.Target == ExecutionTarget.None) throw RemoteUnreachable(nameof(TouchAsync), collectionInfo);
        collectionInfo = exec.Local;

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

        RaiseLocalCollectionInfoChanged(updated);
        _ = Task.Run(() => _cache.SaveAsync(updated));

        // Touch is the operator-driven opportunistic recovery hook: always re-run a fresh
        // index-assure pass on the touched collection. Failed indexes from prior attempts
        // get retried; successful retries clear FailedIndices via the per-index success
        // paths in DiskRepositoryCollectionBase.UpdateIndices*. Cheap for healthy
        // collections (list-existing + compare; no Mongo ops when indexes match).
        try
        {
            await RestoreIndexAsync(collectionInfo, force: true);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "TouchAsync: index re-assure for {Configuration}.{Database}.{Collection} threw — recorded in InitiationLibrary, not propagated.",
                collectionInfo.ConfigurationName, collectionInfo.DatabaseName, collectionInfo.CollectionName);
        }
    }

    public async Task<(int Before, int After)> DropIndexAsync(CollectionInfo collectionInfo)
    {
        if (!_started) throw new InvalidOperationException($"{nameof(DatabaseMonitor)} has not been started. Call {nameof(MongoDbRegistrationExtensions.UseMongoDB)} on application start.");
        if (collectionInfo == null) throw new ArgumentNullException(nameof(collectionInfo));

        var exec = ResolveExecution(collectionInfo);
        if (exec.Target == ExecutionTarget.Remote)
            return await exec.Dispatcher.DropIndexAsync(exec.ConnectionId, collectionInfo);
        if (exec.Target == ExecutionTarget.None) throw RemoteUnreachable(nameof(DropIndexAsync), collectionInfo);
        collectionInfo = exec.Local;

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

        var exec = ResolveExecution(collectionInfo);
        if (exec.Target == ExecutionTarget.Remote)
        {
            await exec.Dispatcher.RestoreIndexAsync(exec.ConnectionId, collectionInfo, force);
            return;
        }
        if (exec.Target == ExecutionTarget.None) throw RemoteUnreachable(nameof(RestoreIndexAsync), collectionInfo);
        collectionInfo = exec.Local;

        var collection = _collectionProvider.GetCollection(collectionInfo.CollectionType, collectionInfo.Registration == Registration.Dynamic ? collectionInfo.ToDatabaseContext() : null);

        var ct = collection.GetType();
        var mongoCollection = await FetchMongoCollection(ct, collection, true);

        var restoreMethod = ct.GetMethod(nameof(DiskRepositoryCollectionBase<EntityBase>.AssureIndex), BindingFlags.Instance | BindingFlags.NonPublic);
        var restoreResult = restoreMethod?.Invoke(collection, [mongoCollection, force, true]);
        var restoreTask = (Task)restoreResult;
        await restoreTask!;

        await UpdateIndexCacheAsync(collectionInfo);
    }

    public async Task<IndexAssureSummary> RestoreAllIndicesAsync(
        Func<CollectionInfo, bool> filter = null,
        IProgress<IndexAssureProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!_started) throw new InvalidOperationException($"{nameof(DatabaseMonitor)} has not been started. Call {nameof(MongoDbRegistrationExtensions.UseMongoDB)} on application start.");

        var instances = new List<CollectionInfo>();
        await foreach (var info in GetInstancesAsync(false, null).WithCancellation(cancellationToken))
        {
            if (filter == null || filter(info)) instances.Add(info);
        }

        var total = instances.Count;
        var succeeded = 0;
        var failed = 0;
        var skipped = 0;

        for (var i = 0; i < total; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = instances[i];

            // Mirrors the guards inside RestoreIndexAsync: collections we know are not in code
            // can't be restored from this side. Report as skipped so the caller can surface it.
            if (info.Registration == Registration.NotInCode)
            {
                skipped++;
                progress?.Report(new IndexAssureProgress { Index = i, Total = total, CollectionInfo = info, Success = false, Skipped = true });
                continue;
            }

            try
            {
                await RestoreIndexAsync(info, force: false);
                succeeded++;
                progress?.Report(new IndexAssureProgress { Index = i, Total = total, CollectionInfo = info, Success = true, Skipped = false });
            }
            catch (Exception e)
            {
                failed++;
                _logger?.LogError(e, "Failed to restore indexes for collection {collection}: {message}", info.CollectionName, e.Message);
                progress?.Report(new IndexAssureProgress { Index = i, Total = total, CollectionInfo = info, Success = false, Skipped = false, Error = e });
            }
        }

        return new IndexAssureSummary { Total = total, Succeeded = succeeded, Failed = failed, Skipped = skipped };
    }

    public async Task<IEnumerable<string[]>> GetIndexBlockersAsync(CollectionInfo collectionInfo, string indexName)
    {
        if (!_started) throw new InvalidOperationException($"{nameof(DatabaseMonitor)} has not been started. Call {nameof(MongoDbRegistrationExtensions.UseMongoDB)} on application start.");
        if (collectionInfo == null) throw new ArgumentNullException(nameof(collectionInfo));

        var exec = ResolveExecution(collectionInfo);
        if (exec.Target == ExecutionTarget.Remote)
            return await exec.Dispatcher.GetIndexBlockersAsync(exec.ConnectionId, collectionInfo, indexName);
        if (exec.Target == ExecutionTarget.None) throw RemoteUnreachable(nameof(GetIndexBlockersAsync), collectionInfo);
        collectionInfo = exec.Local;

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

        var exec = ResolveExecution(collectionInfo);
        if (exec.Target == ExecutionTarget.Remote)
            return await exec.Dispatcher.CleanAsync(exec.ConnectionId, collectionInfo, cleanGuids);
        if (exec.Target == ExecutionTarget.None) throw RemoteUnreachable(nameof(CleanAsync), collectionInfo);
        collectionInfo = exec.Local;

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

        // The clean computed result.SchemaFingerprint from the collection's live entity type (typeof(TEntity)) —
        // that IS the current schema fingerprint. Anchor CurrentSchemaFingerprint to it so "Fingerprint Match"
        // reads Current right after a clean, instead of comparing against a stale/mis-resolved value (the
        // dynamic registration's interface-derived EntityType, or a cache entry that never carried it).
        var updated = _cache.AddOrUpdate(collectionInfo.Key,
            addFactory: _ => collectionInfo with { Clean = result, CurrentSchemaFingerprint = result.SchemaFingerprint },
            updateFactory: (_, existing) => existing with { Clean = result, CurrentSchemaFingerprint = result.SchemaFingerprint });

        RaiseLocalCollectionInfoChanged(updated);

        return result;
    }

    private const int DocumentListLimitCap = 200;
    private const int DocumentListDefaultLimit = 20;
    private const int CompareSchemaSampleCap = 500;
    private const int CompareSchemaDefaultSample = 50;

    public async Task<DocumentDto> GetDocumentAsync(CollectionInfo collectionInfo, string idRaw, CancellationToken cancellationToken = default)
    {
        if (!_started) throw new InvalidOperationException($"{nameof(DatabaseMonitor)} has not been started. Call {nameof(MongoDbRegistrationExtensions.UseMongoDB)} on application start.");
        if (collectionInfo == null) throw new ArgumentNullException(nameof(collectionInfo));
        if (string.IsNullOrEmpty(idRaw)) throw new ArgumentException("id is required.", nameof(idRaw));
        if (collectionInfo.Registration == Registration.NotInCode)
            throw new InvalidOperationException("Document inspection is not supported for remote-only / NotInCode collections in this release.");

        var collection = await GetRawCollectionAsync(collectionInfo);

        var idValue = ParseId(idRaw);
        var filter = Builders<BsonDocument>.Filter.Eq("_id", idValue);
        var doc = await collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
        if (doc == null) return null;

        return new DocumentDto
        {
            Id = doc.TryGetValue("_id", out var idField) ? idField.ToString() : idRaw,
            Json = doc.ToJson(),
        };
    }

    public async Task<DocumentListDto> ListDocumentsAsync(CollectionInfo collectionInfo, DocumentListQuery query, CancellationToken cancellationToken = default)
    {
        if (!_started) throw new InvalidOperationException($"{nameof(DatabaseMonitor)} has not been started. Call {nameof(MongoDbRegistrationExtensions.UseMongoDB)} on application start.");
        if (collectionInfo == null) throw new ArgumentNullException(nameof(collectionInfo));
        if (query == null) query = new DocumentListQuery();
        if (collectionInfo.Registration == Registration.NotInCode)
            throw new InvalidOperationException("Document inspection is not supported for remote-only / NotInCode collections in this release.");

        var collection = await GetRawCollectionAsync(collectionInfo);

        var requested = query.Limit <= 0 ? DocumentListDefaultLimit : query.Limit;
        var limit = Math.Min(requested, DocumentListLimitCap);
        var skip = Math.Max(0, query.Skip);

        FilterDefinition<BsonDocument> filter;
        try
        {
            filter = string.IsNullOrWhiteSpace(query.FilterJson)
                ? FilterDefinition<BsonDocument>.Empty
                : new BsonDocumentFilterDefinition<BsonDocument>(BsonDocument.Parse(query.FilterJson));
        }
        catch (Exception e) when (e is FormatException || e is System.IO.IOException)
        {
            throw new FormatException($"Invalid filter JSON: {e.Message}", e);
        }

        SortDefinition<BsonDocument> sort = null;
        if (!string.IsNullOrWhiteSpace(query.SortJson))
        {
            try
            {
                sort = new BsonDocumentSortDefinition<BsonDocument>(BsonDocument.Parse(query.SortJson));
            }
            catch (Exception e) when (e is FormatException || e is System.IO.IOException)
            {
                throw new FormatException($"Invalid sort JSON: {e.Message}", e);
            }
        }

        var find = collection.Find(filter);
        if (sort != null) find = find.Sort(sort);
        var docs = await find.Skip(skip).Limit(limit).ToListAsync(cancellationToken);

        return new DocumentListDto
        {
            Documents = docs.Select(d => new DocumentDto
            {
                Id = d.TryGetValue("_id", out var idField) ? idField.ToString() : null,
                Json = d.ToJson(),
            }).ToArray(),
            TotalReturned = docs.Count,
            Truncated = docs.Count == limit,
        };
    }

    public async Task<SchemaComparisonDto> CompareSchemaAsync(CollectionInfo collectionInfo, int sampleSize, CancellationToken cancellationToken = default)
    {
        if (!_started) throw new InvalidOperationException($"{nameof(DatabaseMonitor)} has not been started. Call {nameof(MongoDbRegistrationExtensions.UseMongoDB)} on application start.");
        if (collectionInfo == null) throw new ArgumentNullException(nameof(collectionInfo));
        if (collectionInfo.Registration == Registration.NotInCode)
            throw new InvalidOperationException("Document inspection is not supported for remote-only / NotInCode collections in this release.");

        var requested = sampleSize <= 0 ? CompareSchemaDefaultSample : sampleSize;
        var cap = Math.Min(requested, CompareSchemaSampleCap);

        var collection = await GetRawCollectionAsync(collectionInfo);

        var docs = await collection.Find(FilterDefinition<BsonDocument>.Empty).Limit(cap).ToListAsync(cancellationToken);

        var coverage = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var doc in docs)
        {
            foreach (var name in doc.Names)
            {
                coverage.TryGetValue(name, out var c);
                coverage[name] = c + 1;
            }
        }

        var entityType = collectionInfo.CollectionType != null ? ResolveEntityType(collectionInfo.CollectionType) : null;
        var entityProperties = entityType != null
            ? entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

        var fieldNames = new HashSet<string>(coverage.Keys, StringComparer.Ordinal);
        fieldNames.UnionWith(entityProperties);

        var fields = fieldNames
            .Select(name =>
            {
                coverage.TryGetValue(name, out var count);
                var declared = entityProperties.Contains(name);
                SchemaFieldClassification classification;
                if (declared && count == docs.Count && docs.Count > 0) classification = SchemaFieldClassification.Full;
                else if (declared && count == 0) classification = SchemaFieldClassification.EntityOnly;
                else if (!declared && count > 0) classification = SchemaFieldClassification.DocumentOnly;
                else classification = SchemaFieldClassification.Partial;
                return new SchemaComparisonField
                {
                    Name = name,
                    Classification = classification,
                    CoverageCount = count,
                    DeclaredOnEntity = declared,
                };
            })
            .OrderBy(f => f.Name, StringComparer.Ordinal)
            .ToArray();

        return new SchemaComparisonDto
        {
            SampleSize = cap,
            SampledCount = docs.Count,
            EntityTypes = collectionInfo.EntityTypes ?? Array.Empty<string>(),
            Fields = fields,
        };
    }

    private async Task<IMongoCollection<BsonDocument>> GetRawCollectionAsync(CollectionInfo collectionInfo)
    {
        var mongoDbService = GetMongoDbService(collectionInfo);
        return await mongoDbService.GetCollectionAsync(collectionInfo.DatabaseName, collectionInfo.CollectionName);
    }

    private static BsonValue ParseId(string idRaw)
    {
        if (Guid.TryParse(idRaw, out var g)) return new BsonBinaryData(g, GuidRepresentation.Standard);
        if (ObjectId.TryParse(idRaw, out var o)) return o;
        return new BsonString(idRaw);
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

    /// <summary>
    /// The source-identity string used to key all of an agent's data. Equal to the agent's reported
    /// <paramref name="reportedSourceName"/> when unique; disambiguated by the connection's per-process
    /// <c>Instance</c> when another live process already uses that name — so two instances on one machine
    /// with the same name stay separate, and a reconnect (same Instance) keeps the same identity. Without a
    /// connection (local tagging, direct test calls) the reported name is returned unchanged.
    /// </summary>
    private string ResolveEffectiveSource(string connectionId, string reportedSourceName)
    {
        if (string.IsNullOrEmpty(connectionId) || string.IsNullOrEmpty(reportedSourceName))
            return reportedSourceName;

        if (_monitorClients.Values.FirstOrDefault(x => x.ConnectionId == connectionId)?.Instance is not { } instanceId)
            return reportedSourceName;

        if (_instanceEffectiveSource.TryGetValue(instanceId, out var existing))
            return existing;

        lock (_effectiveSourceLock)
        {
            if (_instanceEffectiveSource.TryGetValue(instanceId, out existing))
                return existing;

            var taken = new HashSet<string>(_instanceEffectiveSource.Values, StringComparer.Ordinal);
            var effective = reportedSourceName;
            if (taken.Contains(effective))
            {
                effective = $"{reportedSourceName} ({instanceId.ToString("N")[..4]})";
                if (taken.Contains(effective)) effective = $"{reportedSourceName} ({instanceId})";
            }

            _instanceEffectiveSource[instanceId] = effective;
            return effective;
        }
    }

    public void IngestCall(CallDto call, string connectionId = null)
    {
        var effectiveSource = ResolveEffectiveSource(connectionId, call?.SourceName);
        if (call != null && effectiveSource != call.SourceName)
            call = call with { SourceName = effectiveSource };

        _callLibrary.IngestCall(FromCallDto(call));
        LogComm(effectiveSource, CommunicationDirection.Inbound, "Call",
            $"{call.FunctionName} {call.CollectionName} ({call.Operation}){(call.Final ? "" : " [ongoing]")}");
    }

    public void ResetCalls()
    {
        _callLibrary.ResetCalls();

        // Broadcast to remote agents
        var dispatcher = _serviceProvider.GetService(typeof(IRemoteActionDispatcher)) as IRemoteActionDispatcher;
        if (dispatcher != null)
            _ = dispatcher.ClearCallHistoryAllAsync();
    }

    // --- Remote client management ---

    public IEnumerable<MonitorClientDto> GetMonitorClients()
    {
        return _monitorClients.Values.Select(WithStatus);
    }

    public void IngestClientStatus(string sourceName, MonitorClientStatus status, string connectionId = null)
    {
        if (string.IsNullOrEmpty(sourceName) || status == null) return;
        var effectiveSource = ResolveEffectiveSource(connectionId, sourceName);
        _clientStatus[effectiveSource] = status;
        LogComm(effectiveSource, CommunicationDirection.Inbound, "ClientStatus",
            $"forwarding={status.ForwardCompletedCalls}, queueInterval={status.QueueMetricIntervalMs}ms, commandMonitoring={status.EnableCommandMonitoring}");

        // Correlate the source with its connection so the client entry carries its (effective) SourceName even
        // before it reports a collection — status arrives on connect. Without this, GetMonitorClientDetail can't
        // resolve a just-connected agent and the per-agent detail dialog stays on the loading spinner.
        if (!string.IsNullOrEmpty(connectionId))
        {
            _sourceToConnectionId[effectiveSource] = connectionId;
            var client = _monitorClients.Values.FirstOrDefault(x => x.ConnectionId == connectionId);
            if (client != null && client.SourceName != effectiveSource)
            {
                _monitorClients[client.Instance] = client with { SourceName = effectiveSource };
                MonitorClientsChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public async Task<bool> SetClientCallForwardingAsync(string sourceName, bool enabled, CancellationToken cancellationToken = default)
    {
        if (!_started) throw new InvalidOperationException($"{nameof(DatabaseMonitor)} has not been started. Call {nameof(MongoDbRegistrationExtensions.UseMongoDB)} on application start.");
        if (string.IsNullOrEmpty(sourceName)) throw new ArgumentException("Source name is required.", nameof(sourceName));

        var connectionId = FindConnectionIdBySource(sourceName);
        if (connectionId == null) throw new InvalidOperationException("Agent is not connected.");

        if (_serviceProvider.GetService(typeof(IRemoteActionDispatcher)) is not IRemoteActionDispatcher dispatcher)
            throw new InvalidOperationException("Remote action dispatching is not available.");

        LogComm(sourceName, CommunicationDirection.Outbound, "SetCallForwarding", $"enabled={enabled}");
        var state = await dispatcher.SetCallForwardingAsync(connectionId, enabled, cancellationToken);

        // Reflect immediately; the agent also re-reports its status via IngestClientStatus.
        if (_clientStatus.TryGetValue(sourceName, out var status) && status != null)
            _clientStatus[sourceName] = status with { ForwardCompletedCalls = state };
        MonitorClientsChanged?.Invoke(this, EventArgs.Empty);

        return state;
    }

    public bool CommandMonitoringEnabled =>
        (_serviceProvider.GetService(typeof(Internals.ICommandMonitorService)) as Internals.ICommandMonitorService)?.Enabled ?? false;

    public void SetCommandMonitoring(bool enabled)
    {
        if (_serviceProvider.GetService(typeof(Internals.ICommandMonitorService)) is Internals.ICommandMonitorService svc)
            svc.Enabled = enabled;
    }

    public async Task<bool> SetClientCommandMonitoringAsync(string sourceName, bool enabled, CancellationToken cancellationToken = default)
    {
        if (!_started) throw new InvalidOperationException($"{nameof(DatabaseMonitor)} has not been started. Call {nameof(MongoDbRegistrationExtensions.UseMongoDB)} on application start.");
        if (string.IsNullOrEmpty(sourceName)) throw new ArgumentException("Source name is required.", nameof(sourceName));

        var connectionId = FindConnectionIdBySource(sourceName);
        if (connectionId == null) throw new InvalidOperationException("Agent is not connected.");

        if (_serviceProvider.GetService(typeof(IRemoteActionDispatcher)) is not IRemoteActionDispatcher dispatcher)
            throw new InvalidOperationException("Remote action dispatching is not available.");

        LogComm(sourceName, CommunicationDirection.Outbound, "SetCommandMonitoring", $"enabled={enabled}");
        var state = await dispatcher.SetCommandMonitoringAsync(connectionId, enabled, cancellationToken);

        // Reflect immediately; the agent also re-reports its status.
        if (_clientStatus.TryGetValue(sourceName, out var status) && status != null)
            _clientStatus[sourceName] = status with { EnableCommandMonitoring = state };
        MonitorClientsChanged?.Invoke(this, EventArgs.Empty);

        return state;
    }

    private MonitorClientDto WithStatus(MonitorClientDto client)
    {
        if (!string.IsNullOrEmpty(client.SourceName) && _clientStatus.TryGetValue(client.SourceName, out var status))
            return client with { Status = status };
        return client;
    }

    public IReadOnlyList<CollectionInfo> GetCollectionsWithFailedIndices()
    {
        if (!_started) throw new InvalidOperationException($"{nameof(DatabaseMonitor)} has not been started. Call {nameof(MongoDbRegistrationExtensions.UseMongoDB)} on application start.");

        var initiationLibrary = _serviceProvider.GetService(typeof(Internals.IInitiationLibrary)) as Internals.IInitiationLibrary;
        if (initiationLibrary == null) return [];

        var failures = initiationLibrary.GetCollectionsWithFailures();
        if (failures.Count == 0) return [];

        var lookup = new HashSet<(string Server, string Database, string Collection)>(failures);

        return _cache.GetAll()
            .Where(info => lookup.Contains((info.Server, info.DatabaseName, info.CollectionName)))
            .ToArray();
    }

    public MonitorClientDetail GetMonitorClientDetail(string sourceName, int recentCallLimit = 20)
    {
        if (!_started) throw new InvalidOperationException($"{nameof(DatabaseMonitor)} has not been started. Call {nameof(MongoDbRegistrationExtensions.UseMongoDB)} on application start.");
        if (string.IsNullOrEmpty(sourceName)) return null;

        var client = _monitorClients.Values.FirstOrDefault(x => x.SourceName == sourceName);
        if (client == null) return null;
        client = WithStatus(client);

        var collectionKeys = _collectionSources
            .Where(kvp => kvp.Value.ContainsKey(sourceName))
            .Select(kvp => kvp.Key)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();

        var recentCalls = _callLibrary.GetLastCalls()
            .Where(c => c.SourceName == sourceName)
            .OrderByDescending(c => c.StartTime)
            .Take(recentCallLimit)
            .ToArray();

        ConnectionPoolStateDto queueState = null;
        if (_remoteQueueStates.TryGetValue(sourceName, out var remoteState))
        {
            queueState = new ConnectionPoolStateDto
            {
                QueueCount = remoteState.QueueCount,
                ExecutingCount = remoteState.ExecutingCount,
                LastWaitTimeMs = remoteState.LastWaitTimeMs,
                RecentMetrics = [],
            };
        }

        return new MonitorClientDetail
        {
            Client = client,
            CollectionKeys = collectionKeys,
            RecentCalls = recentCalls,
            QueueState = queueState,
        };
    }

    public void IngestClientConnected(MonitorClientDto client)
    {
        _monitorClients[client.Instance] = client;
        LogComm(client.SourceName, CommunicationDirection.Inbound, "Connected", client.Machine);
        MonitorClientsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void IngestClientDisconnected(string connectionId)
    {
        var entry = _monitorClients.Values.FirstOrDefault(x => x.ConnectionId == connectionId);
        if (entry == null) return;

        _monitorClients[entry.Instance] = entry with { IsConnected = false, DisconnectTime = DateTime.UtcNow };
        LogComm(entry.SourceName, CommunicationDirection.Inbound, "Disconnected", entry.Machine);

        // Drop the disconnected client's source so it no longer counts toward collection
        // reachability or connection/queue metrics. The collection's persisted record is kept — it
        // survives in the _monitor cache with its last-reported age, and reachability gating disables
        // actions until an agent reports it again. (A genuine collection drop, not a disconnect, is
        // what removes the record; see IngestCollectionDropped.)
        var sourceName = entry.SourceName;
        if (!string.IsNullOrEmpty(sourceName))
        {
            if (_sourceToConnectionId.TryGetValue(sourceName, out var mapped) && mapped == connectionId)
                _sourceToConnectionId.TryRemove(sourceName, out _);

            _remoteQueueStates.TryRemove(sourceName, out _);
            _remotePoolStates.TryRemove(sourceName, out _);

            foreach (var kvp in _collectionSources)
            {
                if (kvp.Value.TryRemove(sourceName, out _) && kvp.Value.IsEmpty)
                    _collectionSources.TryRemove(kvp.Key, out _);
            }
        }

        MonitorClientsChanged?.Invoke(this, EventArgs.Empty);
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
            Discovery = discovery | Discovery.Remote,
            Registration = registration,
            EntityTypes = dto.EntityTypes ?? [],
            CollectionType = null,
            CollectionTypeName = dto.CollectionTypeName,
            Stats = dto.Stats,
            Index = dto.Index,
            Clean = dto.Clean,
            ReportedAt = DateTime.UtcNow,
        };

        var effectiveSource = ResolveEffectiveSource(connectionId, dto.SourceName);

        var key = info.Key;
        _cache.Set(key, info);
        _ = Task.Run(() => _cache.SaveAsync(info));
        LogComm(effectiveSource, CommunicationDirection.Inbound, "CollectionInfo",
            $"{dto.DatabaseName}.{dto.CollectionName} [{dto.Registration}]");

        // Track source (by its effective, per-process identity)
        var sources = _collectionSources.GetOrAdd(key, _ => new System.Collections.Concurrent.ConcurrentDictionary<string, bool>());
        sources[effectiveSource] = true;

        // Map source to connection for action delegation
        if (!string.IsNullOrEmpty(connectionId) && !string.IsNullOrEmpty(effectiveSource))
        {
            _sourceToConnectionId[effectiveSource] = connectionId;

            // Carry the effective source name on the client entry so the Clients list and detail dialog
            // resolve the right agent even when two processes share a reported name.
            var client = _monitorClients.Values.FirstOrDefault(x => x.ConnectionId == connectionId);
            if (client != null && client.SourceName != effectiveSource)
                _monitorClients[client.Instance] = client with { SourceName = effectiveSource };
        }

        CollectionInfoChangedEvent?.Invoke(this, new CollectionInfoChangedEventArgs(info));
    }

    public void IngestCollectionDropped(string sourceName, string configurationName, string databaseName, string collectionName, string connectionId = null)
    {
        if (!_started) throw new InvalidOperationException($"{nameof(DatabaseMonitor)} has not been started. Call {nameof(MongoDbRegistrationExtensions.UseMongoDB)} on application start.");
        if (string.IsNullOrEmpty(collectionName) || string.IsNullOrEmpty(databaseName)) return;

        sourceName = ResolveEffectiveSource(connectionId, sourceName);
        LogComm(sourceName, CommunicationDirection.Inbound, "CollectionDropped", $"{databaseName}.{collectionName}");

        // Match by resolved (database, collection): the physical database name plus the physical
        // collection name uniquely identify the collection. Configuration name is intentionally not
        // required — an agent's drop can carry a null/unresolved config (e.g. a dynamic collection
        // whose context didn't set one) while the original report resolved it to a default, and a
        // strict config match would then miss. When a config is supplied, it's used as a soft filter.
        var configName = string.IsNullOrEmpty(configurationName) ? null : configurationName;
        var matches = _cache.GetAll()
            .Where(c => c.DatabaseName == databaseName
                        && c.CollectionName == collectionName
                        && (configName == null || (c.ConfigurationName?.Value ?? _options.DefaultConfigurationName) == configName))
            .ToList();

        foreach (var match in matches)
        {
            var key = match.Key;

            // Drop only this agent's claim. The collection survives while another source still
            // reports it (or the server can reach it locally — its local source stays tagged).
            if (_collectionSources.TryGetValue(key, out var sources) && !string.IsNullOrEmpty(sourceName))
            {
                sources.TryRemove(sourceName, out _);
                if (!sources.IsEmpty) continue;
                _collectionSources.TryRemove(key, out _);
            }

            // A genuine drop means the collection is gone at the source — remove the persisted record too.
            if (_cache.TryRemove(key, out var removed))
            {
                _ = Task.Run(() => _cache.DeleteAsync(removed.DatabaseName, removed.CollectionName));
                CollectionDroppedEvent?.Invoke(this, new CollectionDroppedEventArgs(removed.ToDatabaseContext(),
                    removed.ConfigurationName?.Value, removed.DatabaseName, removed.CollectionName));
            }
        }
    }

    public IReadOnlyCollection<string> GetCollectionSources(string fingerprintKey)
    {
        if (_collectionSources.TryGetValue(fingerprintKey, out var sources))
            return sources.Keys.ToArray();
        return [];
    }

    private void TagLocalSource(string fingerprintKey)
    {
        var localSource = _mongoDbServiceFactory.SourceName;
        if (string.IsNullOrEmpty(localSource)) return;

        var sources = _collectionSources.GetOrAdd(fingerprintKey, _ => new System.Collections.Concurrent.ConcurrentDictionary<string, bool>());
        sources[localSource] = true;
    }

    private void RaiseLocalCollectionInfoChanged(CollectionInfo info)
    {
        TagLocalSource(info.Key);
        CollectionInfoChangedEvent?.Invoke(this, new CollectionInfoChangedEventArgs(info));
    }

    private Internals.ICommunicationLog _communicationLog;
    private Internals.ICommunicationLog CommunicationLog =>
        _communicationLog ??= _serviceProvider.GetService(typeof(Internals.ICommunicationLog)) as Internals.ICommunicationLog;

    private void LogComm(string sourceName, CommunicationDirection direction, string messageType, string summary)
    {
        if (string.IsNullOrEmpty(sourceName)) return;
        CommunicationLog?.Record(sourceName, direction, messageType, summary);
    }

    private string SourceForConnection(string connectionId) =>
        connectionId == null ? null : _monitorClients.Values.FirstOrDefault(x => x.ConnectionId == connectionId)?.SourceName;

    public IReadOnlyList<CommunicationEvent> GetClientCommunication(string sourceName)
    {
        if (!_started) throw new InvalidOperationException($"{nameof(DatabaseMonitor)} has not been started. Call {nameof(MongoDbRegistrationExtensions.UseMongoDB)} on application start.");
        return CommunicationLog?.Get(sourceName) ?? [];
    }

    public void RecordClientCommunication(string sourceName, CommunicationDirection direction, string messageType, string summary)
        => LogComm(sourceName, direction, messageType, summary);

    public string FindConnectionIdBySource(string sourceName)
    {
        if (string.IsNullOrEmpty(sourceName)) return null;
        if (!_sourceToConnectionId.TryGetValue(sourceName, out var connectionId)) return null;

        // Verify the client is still connected
        var client = _monitorClients.Values.FirstOrDefault(x => x.ConnectionId == connectionId && x.IsConnected);
        return client != null ? connectionId : null;
    }

    public IReadOnlyDictionary<string, int> GetSubscriptions()
    {
        var subscription = _serviceProvider.GetService(typeof(ILiveMonitoringSubscription)) as ILiveMonitoringSubscription;
        return subscription?.GetSubscriptions() ?? new Dictionary<string, int>();
    }

    public void IngestQueueMetric(string sourceName, int queueCount, int executingCount, double? waitTimeMs, string connectionId = null)
    {
        sourceName = ResolveEffectiveSource(connectionId, sourceName);
        LogComm(sourceName, CommunicationDirection.Inbound, "QueueMetric", $"Q {queueCount} · E {executingCount} (aggregate)");

        // Legacy aggregate-per-source form. Keep the aggregate (drives GetMonitorClientDetail) and
        // store it as a single synthetic pool so it still surfaces a line in GetPerPoolQueueState.
        _remoteQueueStates[sourceName] = new RemoteQueueState
        {
            QueueCount = queueCount,
            ExecutingCount = executingCount,
            LastWaitTimeMs = waitTimeMs ?? 0,
            Timestamp = DateTime.UtcNow,
        };
        _remotePoolStates[sourceName] = new[]
        {
            new PoolMetricDto
            {
                ServerKey = string.Empty,
                ConfigurationNames = Array.Empty<string>(),
                QueueCount = queueCount,
                ExecutingCount = executingCount,
                WaitTimeMs = waitTimeMs,
            }
        };
    }

    public void IngestQueueMetric(string sourceName, IReadOnlyList<PoolMetricDto> pools, string connectionId = null)
    {
        sourceName = ResolveEffectiveSource(connectionId, sourceName);
        pools ??= Array.Empty<PoolMetricDto>();
        LogComm(sourceName, CommunicationDirection.Inbound, "QueueMetric",
            $"{pools.Count} pool(s) · Q {pools.Sum(p => p.QueueCount)} · E {pools.Sum(p => p.ExecutingCount)} · open {pools.Sum(p => p.OpenConnections)}");
        _remotePoolStates[sourceName] = pools;
        // Maintain the aggregate per-source view for GetMonitorClientDetail.
        _remoteQueueStates[sourceName] = new RemoteQueueState
        {
            QueueCount = pools.Sum(p => p.QueueCount),
            ExecutingCount = pools.Sum(p => p.ExecutingCount),
            LastWaitTimeMs = pools.Count == 0 ? 0 : pools.Max(p => p.WaitTimeMs ?? 0),
            Timestamp = DateTime.UtcNow,
        };
    }

    public IReadOnlyDictionary<string, ConnectionPoolStateDto> GetPerPoolQueueState()
    {
        var result = new Dictionary<string, ConnectionPoolStateDto>();

        var localSource = _mongoDbServiceFactory.SourceName;
        var localPools = _queueMonitor.GetPerPoolState(); // reads + resets per-pool wait, so call once

        // Suffix labels with the source when more than one source actually contributes pools, to keep lines distinct.
        var contributingSources = new HashSet<string>();
        if (localPools.Count > 0) contributingSources.Add(localSource);
        foreach (var (source, pools) in _remotePoolStates)
            if (pools.Count > 0) contributingSources.Add(source);
        var multiSource = contributingSources.Count > 1;

        // Local — one entry per physical pool.
        foreach (var pool in localPools)
        {
            var key = $"{localSource}::{pool.ServerKey}";
            result[key] = new ConnectionPoolStateDto
            {
                QueueCount = pool.QueueCount,
                ExecutingCount = pool.ExecutingCount,
                LastWaitTimeMs = pool.LastWaitTimeMs,
                RecentMetrics = [],
                ConfigurationNames = pool.ConfigurationNames,
                Label = BuildPoolLabel(pool.ConfigurationNames, pool.ServerKey, localSource, multiSource),
            };
        }

        // Remote — one entry per reported pool.
        foreach (var (source, pools) in _remotePoolStates)
        {
            foreach (var pool in pools)
            {
                var key = $"{source}::{pool.ServerKey}";
                result[key] = new ConnectionPoolStateDto
                {
                    QueueCount = pool.QueueCount,
                    ExecutingCount = pool.ExecutingCount,
                    LastWaitTimeMs = pool.WaitTimeMs ?? 0,
                    RecentMetrics = [],
                    ConfigurationNames = pool.ConfigurationNames,
                    Label = BuildPoolLabel(pool.ConfigurationNames, pool.ServerKey, source, multiSource),
                };
            }
        }

        return result;
    }

    public IReadOnlyList<InFlightCallInfo> GetInFlightCalls() => _queueMonitor.GetInFlightCalls();

    public IReadOnlyList<ClusterConnectionSummary> GetClusterConnectionSummary()
    {
        // Aggregate actual open connections + capacity per cluster (host) across every source: this
        // process's own pools plus all reporting agents. cluster -> serverKey(pool) -> source.
        var byCluster = new Dictionary<string, Dictionary<string, PoolAccumulator>>();

        PoolAccumulator GetPool(string serverKey)
        {
            // Group pools under their cluster (server host(s)); pools that differ only in max pool size
            // collapse to the same cluster but stay distinct pools.
            var cluster = MongoDbClientProvider.ClusterOf(serverKey);
            if (!byCluster.TryGetValue(cluster, out var poolsByKey))
                byCluster[cluster] = poolsByKey = new Dictionary<string, PoolAccumulator>();
            if (!poolsByKey.TryGetValue(serverKey, out var acc))
                poolsByKey[serverKey] = acc = new PoolAccumulator();
            return acc;
        }

        void AddSource(string serverKey, string source, int open, int max, IEnumerable<string> configNames, int queue, int exec)
        {
            var acc = GetPool(serverKey);
            var s = acc.SourceByName.TryGetValue(source, out var existing) ? existing : acc.SourceByName[source] = new SourceAccumulator();
            s.Open += open;
            s.Max += max;
            s.Queue += queue;
            s.Exec += exec;
            if (configNames != null)
                foreach (var n in configNames) acc.ConfigurationNames.Add(n);
        }

        // Local — config-name labels and queue/exec come from the limiter's per-pool view (read once;
        // reading resets the wait-time high-water mark). May be empty if no calls have run yet.
        var localSource = _mongoDbServiceFactory.SourceName;
        var localByServer = _queueMonitor.GetPerPoolState()
            .ToDictionary(p => p.ServerKey, p => p);
        foreach (var pool in _connectionPoolMonitor.GetSnapshot())
        {
            localByServer.TryGetValue(pool.ServerKey, out var q);
            AddSource(pool.ServerKey, localSource, pool.OpenConnections, pool.MaxPoolSize,
                q?.ConfigurationNames, q?.QueueCount ?? 0, q?.ExecutingCount ?? 0);
        }

        // Remote — each agent's reported per-pool connection + queue counts.
        foreach (var (source, pools) in _remotePoolStates)
            foreach (var pool in pools)
                AddSource(pool.ServerKey, source, pool.OpenConnections, pool.MaxPoolSize,
                    pool.ConfigurationNames, pool.QueueCount, pool.ExecutingCount);

        var resolver = _options.Monitor.ClusterConnectionLimitResolver;
        var globalLimit = _options.Monitor.ClusterConnectionLimit;
        return byCluster
            .OrderBy(c => c.Key, StringComparer.Ordinal)
            .Select(c =>
            {
                var pools = c.Value.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p =>
                {
                    var sources = p.Value.SourceByName
                        .OrderBy(s => s.Key, StringComparer.Ordinal)
                        .Select(s => new ClusterPoolSourceConnections
                        {
                            Source = s.Key,
                            OpenConnections = s.Value.Open,
                            MaxPoolSize = s.Value.Max,
                            QueueCount = s.Value.Queue,
                            ExecutingCount = s.Value.Exec,
                        })
                        .ToArray();
                    return new ClusterPoolSummary
                    {
                        ServerKey = p.Key,
                        // Every source on one server-key shares the same max pool size (it is part of the key).
                        MaxPoolSize = sources.Length > 0 ? sources[0].MaxPoolSize : 0,
                        ConfigurationNames = p.Value.ConfigurationNames.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                        SourceCount = sources.Length,
                        OpenConnections = sources.Sum(s => s.OpenConnections),
                        QueueCount = sources.Sum(s => s.QueueCount),
                        ExecutingCount = sources.Sum(s => s.ExecutingCount),
                        Sources = sources,
                    };
                }).ToArray();

                var configNames = pools.SelectMany(p => p.ConfigurationNames).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray();

                // Per-cluster limit: resolver first (e.g. a tier lookup or a runtime-updated value), then the
                // single global fallback, then "no limit" (null) — which renders as a total with no bar.
                int? limit = null;
                if (resolver != null)
                {
                    var ctx = new ClusterConnectionLimitContext
                    {
                        Cluster = c.Key,
                        IsAtlas = MongoDbClientProvider.IsAtlasCluster(c.Key),
                        ConfigurationNames = configNames,
                    };
                    try { limit = resolver(_serviceProvider, ctx); }
                    catch (Exception ex) { _logger?.LogDebug(ex, "ClusterConnectionLimitResolver threw for cluster {Cluster}.", c.Key); }
                }
                limit ??= globalLimit;

                return new ClusterConnectionSummary
                {
                    Cluster = c.Key,
                    IsAtlas = MongoDbClientProvider.IsAtlasCluster(c.Key),
                    ConfigurationNames = configNames,
                    SourceCount = pools.SelectMany(p => p.Sources.Select(s => s.Source)).Distinct().Count(),
                    OpenConnections = pools.Sum(p => p.OpenConnections),
                    MaxConnections = pools.Sum(p => p.Sources.Sum(s => s.MaxPoolSize)),
                    Limit = limit,
                    Pools = pools,
                };
            })
            .ToArray();
    }

    private sealed class PoolAccumulator
    {
        public readonly Dictionary<string, SourceAccumulator> SourceByName = new();
        public readonly HashSet<string> ConfigurationNames = new();
    }

    private sealed class SourceAccumulator
    {
        public int Open;
        public int Max;
        public int Queue;
        public int Exec;
    }

    private static string BuildPoolLabel(IReadOnlyCollection<string> configurationNames, string serverKey, string source, bool multiSource)
    {
        var baseLabel = configurationNames is { Count: > 0 }
            ? string.Join(", ", configurationNames.OrderBy(x => x, StringComparer.Ordinal))
            : string.IsNullOrEmpty(serverKey) ? source : serverKey;

        return multiSource ? $"{baseLabel} @ {source}" : baseLabel;
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
        if (call?.ExplainProvider != null)
            return await call.ExplainProvider(cancellationToken);

        // Remote call — delegate to the agent that produced it
        if (call?.SourceName != null)
        {
            var connectionId = FindConnectionIdBySource(call.SourceName);
            var dispatcher = _serviceProvider.GetService(typeof(IRemoteActionDispatcher)) as IRemoteActionDispatcher;
            if (connectionId != null && dispatcher != null)
                return await dispatcher.GetExplainAsync(connectionId, callKey, cancellationToken);
        }

        return null;
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

    private enum ExecutionTarget { None, Local, Remote }

    private static InvalidOperationException RemoteUnreachable(string operation, CollectionInfo info)
    {
        return info.Registration == Registration.NotInCode
            ? new InvalidOperationException($"{operation} does not support {nameof(Registration)} {info.Registration}.")
            : new InvalidOperationException("Collection cannot be actioned: it is not available in this process and no connected agent reports it.");
    }

    /// <summary>
    /// Decides how an action on <paramref name="collectionInfo"/> can be serviced right now:
    /// directly by this process when it has the collection in code and the configuration,
    /// otherwise by a currently-connected agent that reports it. A <see cref="Registration.NotInCode"/>
    /// collection can be serviced by neither, since no side has code to run against it.
    /// </summary>
    private (ExecutionTarget Target, CollectionInfo Local, IRemoteActionDispatcher Dispatcher, string ConnectionId) ResolveExecution(CollectionInfo collectionInfo)
    {
        var local = TryLocalize(collectionInfo);
        if (local != null && local.Registration != Registration.NotInCode)
            return (ExecutionTarget.Local, local, null, null);

        // NotInCode can't be actioned anywhere — don't bother dispatching.
        if (collectionInfo.Registration != Registration.NotInCode)
        {
            var (dispatcher, connectionId) = TryGetRemoteDispatcher(collectionInfo);
            if (dispatcher != null)
                return (ExecutionTarget.Remote, null, dispatcher, connectionId);
        }

        return (ExecutionTarget.None, null, null, null);
    }

    /// <summary>
    /// Returns a version of <paramref name="info"/> that this process can execute against — i.e.
    /// with <see cref="CollectionInfo.CollectionType"/> resolved from local registrations — or null
    /// when this process can't reach it (config not registered here, or collection not in code).
    /// Runtime-reported stats/index/clean from the original entry are preserved.
    /// </summary>
    private CollectionInfo TryLocalize(CollectionInfo info)
    {
        if (info == null) return null;
        if (info.CollectionType != null) return info; // already locally executable

        var configName = info.ConfigurationName?.Value ?? _options.DefaultConfigurationName;
        if (!GetConfigurations().Any(c => (c.Value ?? _options.DefaultConfigurationName) == configName))
            return null;

        var resolved = BuildInitialEntry(info, info.Server, info.DatabasePart, info.EntityTypes?.FirstOrDefault());
        if (resolved.CollectionType == null) return null; // config is here, but the collection is not in code

        return resolved with
        {
            Stats = info.Stats ?? resolved.Stats,
            Index = info.Index ?? resolved.Index,
            Clean = info.Clean ?? resolved.Clean,
            Discovery = info.Discovery | resolved.Discovery,
        };
    }

    /// <summary>
    /// Whether any action (touch/clean/index) on this collection can currently be serviced —
    /// either locally by this process or by a connected agent. Used by the UI to gate action buttons.
    /// </summary>
    public bool CanExecuteActions(CollectionInfo collectionInfo)
    {
        if (!_started) throw new InvalidOperationException($"{nameof(DatabaseMonitor)} has not been started. Call {nameof(MongoDbRegistrationExtensions.UseMongoDB)} on application start.");
        if (collectionInfo == null) return false;
        return ResolveExecution(collectionInfo).Target != ExecutionTarget.None;
    }

    private (IRemoteActionDispatcher Dispatcher, string ConnectionId) TryGetRemoteDispatcher(CollectionInfo collectionInfo)
    {
        var dispatcher = _serviceProvider.GetService(typeof(IRemoteActionDispatcher)) as IRemoteActionDispatcher;
        if (dispatcher == null)
        {
            _logger?.LogDebug("TryGetRemoteDispatcher: IRemoteActionDispatcher not registered.");
            return (null, null);
        }

        var sources = GetCollectionSources(collectionInfo.Key);
        _logger?.LogDebug("TryGetRemoteDispatcher: Collection {Key} has {Count} sources: [{Sources}]", collectionInfo.Key, sources.Count, string.Join(", ", sources));

        // Spread action load across every connected agent that reports this collection rather than always
        // hammering the first one. (Local execution is preferred earlier, in ResolveExecution.)
        var connectionIds = sources
            .Select(FindConnectionIdBySource)
            .Where(id => id != null)
            .ToArray();
        if (connectionIds.Length == 0) return (null, null);

        var connectionId = connectionIds[Random.Shared.Next(connectionIds.Length)];
        _logger?.LogDebug("TryGetRemoteDispatcher: picked ConnectionId {ConnectionId} of {Count} connected source(s).", connectionId, connectionIds.Length);
        return (dispatcher, connectionId);
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
        var (staticLookup, dynamicLookup, dynamicByNameLookup) = GetLookups();
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

        // Resolve the dynamic registration either by the entity-type name (learned from a live
        // access or the persisted cache) or, failing that, by collection name — the latter lets
        // a default-named dynamic collection be classified before it's ever accessed in-process.
        DynColInfo dyn = null;
        var resolvedEntityTypeName = entityTypeName;
        if (entityTypeName != null) dynamicLookup.TryGetValue(entityTypeName, out dyn);
        if (dyn == null && dynamicByNameLookup.TryGetValue((configName, fingerprint.CollectionName), out var byName))
        {
            dyn = byName;
            resolvedEntityTypeName = byName.Type;
        }

        if (dyn != null)
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
                EntityTypes = [resolvedEntityTypeName],
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

    private (Dictionary<(string, string), StatColInfo> staticLookup, Dictionary<string, DynColInfo> dynamicLookup, Dictionary<(string, string), DynColInfo> dynamicByNameLookup) GetLookups()
    {
        if (_staticLookup != null) return (_staticLookup, _dynamicLookup, _dynamicByNameLookup);

        _lookupLock.Wait();
        try
        {
            if (_staticLookup != null) return (_staticLookup, _dynamicLookup, _dynamicByNameLookup);

            var staticLookup = BuildStaticLookup(GetStaticCollectionsFromCodeCore(), _options.DefaultConfigurationName);
            var a = GetDynamicRegistrations(staticLookup.Select(x => new DatabaseContext { ConfigurationName = x.Key.Item1 })).ToArray();
            var b = a.GroupBy(x => x.Type).ToArray();
            var c = b.Where(x => x.Count() > 1).ToArray();
            var d = b.Select(x => x.First());
            _dynamicLookup = d.ToDictionary(x => x.Type, x => x);

            // Name-keyed dynamic lookup: lets a collection discovered purely from the database
            // (never accessed in-code in this process lifetime) be classified as Dynamic on first
            // sight, as long as it uses the default collection name. Keyed by (config, collection
            // name) to mirror the static lookup; a dynamic registration that overrides its name
            // per-context can't be resolved this way and falls back to persist-on-use.
            _dynamicByNameLookup = BuildDynamicByNameLookup(a, _options.DefaultConfigurationName);

            // Assign the field that gates initialization last, so a concurrent reader either sees
            // all lookups or takes the lock.
            _staticLookup = staticLookup;

            return (_staticLookup, _dynamicLookup, _dynamicByNameLookup);
        }
        finally
        {
            _lookupLock.Release();
        }
    }

    public async Task ResetAsync()
    {
        if (!_started) throw new InvalidOperationException($"{nameof(DatabaseMonitor)} has not been started. Call {nameof(MongoDbRegistrationExtensions.UseMongoDB)} on application start.");
        // Clears both persisted records and remotely-reported entries (they now share the cache).
        // Connected agents are asked to re-send below, which repopulates these with fresh data.
        await _cache.ResetAsync();

        // Source/connection maps for live agents are kept so their re-reports can still be correlated
        // and delegated.
        _collectionSources.Clear();

        // Broadcast to remote agents (clears their cache and triggers a fresh collection-info re-send).
        var dispatcher = _serviceProvider.GetService(typeof(IRemoteActionDispatcher)) as IRemoteActionDispatcher;
        if (dispatcher != null)
            await dispatcher.ResetCacheAllAsync();
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

        RaiseLocalCollectionInfoChanged(updated);
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

            _logger?.LogDebug("Loaded collection {Collection} [{Elapsed:N0}s]", meta.CollectionName, sw.Elapsed.TotalSeconds);

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
                        CurrentSchemaFingerprint = SchemaFingerprint.IsCurrentVersion(cached.CurrentSchemaFingerprint)
                            ? cached.CurrentSchemaFingerprint
                            : codeEntry.CurrentSchemaFingerprint,
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

    /// <summary>
    /// Builds the static-collection lookup. Two registered classes are legitimately
    /// allowed to overlay the same physical Mongo collection as read projections
    /// (e.g. a writer + a lean read-projection class). Same key would otherwise crash
    /// <c>ToDictionary</c>; group-and-take-first matches the dynamic-lookup pattern
    /// right below it, and the merged <c>EntityTypes</c> let the monitor UI surface
    /// every reader of the shared physical collection.
    /// </summary>
    internal static Dictionary<(string, string), StatColInfo> BuildStaticLookup(IEnumerable<StatColInfo> source, string defaultConfigurationName)
    {
        return source
            .GroupBy(x => (x.ConfigurationName ?? defaultConfigurationName, x.CollectionName))
            .ToDictionary(
                g => g.Key,
                g => g.First() with { EntityTypes = g.SelectMany(x => x.EntityTypes).Distinct().ToArray() });
    }

    /// <summary>
    /// Builds the name-keyed dynamic lookup used to classify a collection seen only via a database
    /// scan (never accessed in-code) as <see cref="Registration.Dynamic"/>. Keyed by
    /// (configuration, collection name) to mirror <see cref="BuildStaticLookup"/>. Entries without a
    /// resolvable collection name (e.g. dynamic registrations that name themselves per-context) are
    /// skipped; duplicate keys collapse to the first, matching the static/dynamic lookup pattern.
    /// </summary>
    internal static Dictionary<(string, string), DynColInfo> BuildDynamicByNameLookup(IEnumerable<DynColInfo> source, string defaultConfigurationName)
    {
        return source
            .Where(x => !string.IsNullOrEmpty(x.CollectionName))
            .GroupBy(x => (x.ConfigurationName ?? defaultConfigurationName, x.CollectionName))
            .ToDictionary(g => g.Key, g => g.First());
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
                            ConfigurationName = databaseContext.ConfigurationName,
                            CollectionName = collection.CollectionName,
                        };
                    }
                    else
                    {
                        _logger?.LogDebug("Skipping dynamic registration for {Collection}: entity type could not be resolved.", registeredCollection.Key.Name);
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
        public string ConfigurationName { get; init; }
        public string CollectionName { get; init; }
    }
}
