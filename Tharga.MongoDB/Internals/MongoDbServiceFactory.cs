using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Tharga.MongoDB.Atlas;
using Tharga.MongoDB.Configuration;
using Tharga.MongoDB.Interception;

namespace Tharga.MongoDB.Internals;

internal class MongoDbServiceFactory : IMongoDbServiceFactory
{
    private readonly IMongoDbClientProvider _mongoDbClientProvider;
    private readonly IRepositoryConfigurationLoader _repositoryConfigurationLoader;
    private readonly IMongoDbFirewallStateService _mongoDbFirewallStateService;
    private readonly IExecuteLimiter _executeLimiter;
    private readonly ICollectionPool _collectionPool;
    private readonly IInitiationLibrary _initiationLibrary;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, MongoDbService> _databaseDbServices = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private ICollectionInterceptor[] _interceptors = [];

    private static readonly string DefaultSourceName = $"{Environment.MachineName}/{Assembly.GetEntryAssembly()?.GetName().Name ?? "Unknown"}";

    public MongoDbServiceFactory(IMongoDbClientProvider mongoDbClientProvider, IRepositoryConfigurationLoader repositoryConfigurationLoader, IMongoDbFirewallStateService mongoDbFirewallStateService, IExecuteLimiter executeLimiter, ICollectionPool collectionPool, IInitiationLibrary initiationLibrary, ILogger<MongoDbServiceFactory> logger)
    {
        _mongoDbClientProvider = mongoDbClientProvider;
        _repositoryConfigurationLoader = repositoryConfigurationLoader;
        _mongoDbFirewallStateService = mongoDbFirewallStateService;
        _executeLimiter = executeLimiter;
        _collectionPool = collectionPool;
        _initiationLibrary = initiationLibrary;
        _logger = logger;
        SourceName = DefaultSourceName;
    }

    public string SourceName { get; internal set; }
    public bool AllowDelayedCommit { get; internal set; } = true;
    internal ICommandMonitorService CommandMonitor { get; set; }

    /// <summary>
    /// The ordered interceptor chain for this container. Deliberately held on the concrete factory
    /// rather than on <see cref="IMongoDbServiceFactory"/> — the interface is public, so adding a
    /// member would break consumers implementing it. Same pattern as
    /// <see cref="CommandMonitor"/> and <see cref="RecordingState"/>.
    /// </summary>
    internal ICollectionInterceptor[] Interceptors
    {
        get => _interceptors;
        set
        {
            _interceptors = value ?? [];
            HasInvocationInterceptors = _interceptors.Any(x => x.Points.HasFlag(InterceptionPoint.Invocation));
            HasEnumerationInterceptors = _interceptors.Any(x => x.Points.HasFlag(InterceptionPoint.Enumeration));
        }
    }

    /// <summary>Precomputed so the call hot path costs a field read when nothing is registered.</summary>
    internal bool HasInvocationInterceptors { get; private set; }

    /// <summary>Precomputed so a deferred operation skips the enumeration pass entirely when unused.</summary>
    internal bool HasEnumerationInterceptors { get; private set; }

    /// <summary>Runtime gate for how much per-call data is recorded. Null = record everything (back-compat).</summary>
    internal MonitorRecordingState RecordingState { get; set; }

    public event EventHandler<CollectionAccessEventArgs> CollectionAccessEvent;
    public event EventHandler<IndexUpdatedEventArgs> IndexUpdatedEvent;
    public event EventHandler<CollectionDroppedEventArgs> CollectionDroppedEvent;
    public event EventHandler<CallStartEventArgs> CallStartEvent;
    public event EventHandler<CallEndEventArgs> CallEndEvent;

    public IMongoDbService GetMongoDbService(Func<DatabaseContext> databaseContextLoader)
    {
        var configuration = _repositoryConfigurationLoader.GetConfiguration(databaseContextLoader);

        var mongoUrl = configuration.GetDatabaseUrl();
        var cacheKey = mongoUrl.Url;

        if (_databaseDbServices.TryGetValue(cacheKey, out var dbService)) return dbService;

        _lock.Wait();
        try
        {
            if (_databaseDbServices.TryGetValue(cacheKey, out dbService)) return dbService;

            dbService = new MongoDbService(configuration, _mongoDbFirewallStateService, _mongoDbClientProvider, _executeLimiter, _collectionPool, _initiationLibrary, _logger);
            dbService.CollectionAccessEvent += (s, e) =>
            {
                CollectionAccessEvent?.Invoke(s, e);
            };
            _databaseDbServices.TryAdd(cacheKey, dbService);
            return dbService;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void OnIndexUpdatedEvent(object sender, IndexUpdatedEventArgs e)
    {
        Task.Run(() =>
        {
            IndexUpdatedEvent?.Invoke(sender, e);
        });
    }

    public void OnCollectionDropped(object sender, CollectionDroppedEventArgs e)
    {
        Task.Run(() =>
        {
            CollectionDroppedEvent?.Invoke(sender, e);
        });
    }

    public void OnCallStart(object sender, CallStartEventArgs e)
    {
        // Recording gate: skip entirely when nothing consumes calls (CallRecordingLevel.WhenConsumed while idle).
        if (RecordingState is { ShouldRecord: false }) return;
        CallStartEvent?.Invoke(sender, e);
    }

    public void OnCallEnd(object sender, CallEndEventArgs e)
    {
        var state = RecordingState;
        if (state != null)
        {
            if (!state.ShouldRecord) return;
            // OnDemand while idle: keep the lightweight call, drop the (unconsumed) step timeline.
            if (!state.ShouldRecordSteps && e.Steps is { Count: > 0 })
                e = new CallEndEventArgs(e.CallKey, e.Elapsed, e.Exception, e.Count, steps: [], e.FilterJsonProvider, e.ExplainProvider, e.Final);
        }
        CallEndEvent?.Invoke(sender, e);
    }
}