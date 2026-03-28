using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Tharga.MongoDB.Atlas;
using Tharga.MongoDB.Configuration;

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

        //TODO: Should cache be used here or not?
        var useCache = true;

        if (useCache && _databaseDbServices.TryGetValue(cacheKey, out var dbService)) return dbService;

        _lock.Wait();
        try
        {
            if (useCache && _databaseDbServices.TryGetValue(cacheKey, out dbService)) return dbService;

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
        CallStartEvent?.Invoke(sender, e);
    }

    public void OnCallEnd(object sender, CallEndEventArgs e)
    {
        CallEndEvent?.Invoke(sender, e);
    }
}