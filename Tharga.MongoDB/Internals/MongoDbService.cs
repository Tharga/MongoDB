using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tharga.MongoDB.Atlas;
using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB.Internals;

internal class MongoDbService : IMongoDbService
{
    private readonly IRepositoryConfigurationInternal _configuration;
    private readonly IMongoDbFirewallStateService _mongoDbFirewallStateService;
    private readonly ILogger _logger;
    private readonly MongoClient _mongoClient;
    private readonly IMongoDatabase _mongoDatabase;
    private readonly MongoUrl _mongoUrl;

    public MongoDbService(IRepositoryConfigurationInternal configuration, IMongoDbFirewallStateService mongoDbFirewallStateService, IMongoDbClientProvider mongoDbClientProvider, IExecuteLimiter executeLimiter, ICollectionPool collectionPool, IInitiationLibrary initiationLibrary, ILogger logger)
    {
        _configuration = configuration;
        _mongoDbFirewallStateService = mongoDbFirewallStateService;
        ExecuteLimiter = executeLimiter;
        CollectionPool = collectionPool;
        InitiationLibrary = initiationLibrary;
        _logger = logger;
        _mongoUrl = configuration.GetDatabaseUrl() ?? throw new NullReferenceException("MongoUrl not found in configuration.");
        _mongoClient = mongoDbClientProvider.GetClient(_mongoUrl);
        var settings = new MongoDatabaseSettings { WriteConcern = WriteConcern.WMajority };
        _mongoDatabase = _mongoClient.GetDatabase(_mongoUrl.DatabaseName, settings);
    }

    public event EventHandler<CollectionAccessEventArgs> CollectionAccessEvent;

    public IExecuteLimiter ExecuteLimiter { get; }
    public ICollectionPool CollectionPool { get; }
    public IInitiationLibrary InitiationLibrary { get; }

    public async Task<IMongoCollection<T>> GetCollectionAsync<T>(string name)
    {
        await AssureFirewallAccessAsync();

        var collection = _mongoDatabase.GetCollection<T>(name);
        var databaseContext = _configuration.GetDatabaseContext();
        var fingerprint = new CollectionFingerprint
        {
            ConfigurationName = databaseContext.ConfigurationName?.Value ?? _configuration.GetConfigurationName(),
            DatabaseName = _mongoDatabase.DatabaseNamespace.DatabaseName,
            CollectionName = name
        };
        CollectionAccessEvent?.Invoke(this, new CollectionAccessEventArgs(fingerprint, _mongoUrl.Url, typeof(T), databaseContext.DatabasePart));

        return collection;
    }

    public async Task<IMongoCollection<T>> CreateCollectionAsync<T>(string name)
    {
        await _mongoDatabase.CreateCollectionAsync(name);
        return await GetCollectionAsync<T>(name);
    }

    public string GetConfigurationName()
    {
        return _configuration.GetDatabaseContext()?.ConfigurationName?.Value ?? _configuration.GetConfigurationName();
    }

    public async ValueTask<string> AssureFirewallAccessAsync(bool force = false)
    {
        if (_configuration.GetDatabaseUrl().Server.Host.Contains("localhost", StringComparison.InvariantCultureIgnoreCase)) return null;
        var message = await _mongoDbFirewallStateService.AssureFirewallAccessAsync(_configuration.GetConfiguration().AccessInfo, force);
        _logger.LogDebug(message);
        return message;
    }

    public LogLevel GetExecuteInfoLogLevel()
    {
        return _configuration?.GetExecuteInfoLogLevel() ?? LogLevel.Debug;
    }

    public AssureIndexMode GetAssureIndexMode()
    {
        return _configuration?.GetAssureIndexMode() ?? AssureIndexMode.ByName;
    }

    public string GetDatabaseName()
    {
        return _mongoDatabase.DatabaseNamespace.DatabaseName;
    }

    public string GetDatabaseAddress()
    {
        return $"{_mongoClient.Settings.Server.Host}:{_mongoClient.Settings.Server.Port}";
    }

    public int GetMaxConnectionPoolSize()
    {
        return _mongoClient.Settings.MaxConnectionPoolSize;
    }

    public string GetDatabaseHostName()
    {
        return _mongoClient.Settings.Server.Host;
    }

    public Task DropCollectionAsync(string name)
    {
        return _mongoDatabase.DropCollectionAsync(name);
    }

    public IEnumerable<string> GetCollections()
    {
        return _mongoDatabase.ListCollections().ToEnumerable().Select(x => x.AsBsonValue["name"].ToString());
    }

    public async IAsyncEnumerable<CollectionMeta> GetCollectionsWithMetaAsync(string databaseName = null)
    {
        var mongoDatabase = string.IsNullOrEmpty(databaseName) ? _mongoDatabase : _mongoClient.GetDatabase(databaseName);
        var collections = (await mongoDatabase.ListCollectionsAsync()).ToEnumerable();

        foreach (var collection in collections)
        {
            var collectionName = collection["name"].AsString;
            var mongoCollection = mongoDatabase.GetCollection<BsonDocument>(collectionName);

            var types = await mongoCollection
                .Distinct<string>("_t", FilterDefinition<BsonDocument>.Empty)
                .ToListAsync();

            var documentCount = await mongoCollection.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty);
            var size = GetSize(collectionName, mongoDatabase);

            var indexModels = await BuildIndicesModel(mongoCollection);

            var dbName = mongoDatabase.DatabaseNamespace.DatabaseName;
            var server = ToServerName(dbName);

            yield return new CollectionMeta
            {
                ConfigurationName = _configuration.GetConfigurationName(),
                DatabaseName = dbName,
                CollectionName = collectionName,
                Server = server,
                DocumentCount = documentCount,
                Size = size,
                Types = types.ToArray(),
                Indexes = indexModels
                    .Where(x => !x.Name.StartsWith("_id_"))
                    .ToArray(),
            };
        }
    }

    internal static async Task<List<IndexMeta>> BuildIndicesModel<T>(IMongoCollection<T> collection)
    {
        var indexModels = new List<IndexMeta>();
        using var cursor = await collection.Indexes.ListAsync();
        var indexDocs = await cursor.ToListAsync();

        foreach (var indexDoc in indexDocs)
        {
            var indexName = indexDoc.GetValue("name", BsonNull.Value).AsString;
            var isUnique = indexDoc.TryGetValue("unique", out var uniqueVal) && uniqueVal.AsBoolean;
            var keyDoc = indexDoc["key"].AsBsonDocument;

            var fields = keyDoc.Names.ToArray();

            indexModels.Add(new IndexMeta
            {
                Name = indexName,
                Fields = fields,
                IsUnique = isUnique
            });
        }

        return indexModels;
    }

    private string ToServerName(string dbName)
    {
        var server = _mongoUrl.Url.TrimEnd(dbName);
        server = server.Trim('/');
        var p = server.LastIndexOf("//", StringComparison.Ordinal);
        server = server.Substring(p + 2);
        return server;
    }

    public async Task<bool> DoesCollectionExist(string name)
    {
        var filter = new BsonDocument("name", name);
        var options = new ListCollectionNamesOptions { Filter = filter };
        var exists = await (await _mongoDatabase.ListCollectionNamesAsync(options)).AnyAsync();
        return exists;
    }

    public long GetSize(string collectionName, IMongoDatabase mongoDatabase = null)
    {
        var size = (mongoDatabase ?? _mongoDatabase).RunCommand<SizeResult>($"{{collstats: '{collectionName}'}}").Size;
        return size;
    }

    public async Task<DatabaseInfo> GetInfoAsync(bool assureFirewall = true)
    {
        try
        {
            var firewallMessage = string.Empty;
            if (assureFirewall)
            {
                firewallMessage = await AssureFirewallAccessAsync();
            }

            return new DatabaseInfo
            {
                CanConnect = true,
                Message = GetDatabaseDescription(),
                Firewall = firewallMessage,
                CollectionCount = GetCollections().Count()
            };
        }
        catch (Exception e)
        {
            return new DatabaseInfo
            {
                Message = e.Message
            };
        }
    }

    public int? GetResultLimit()
    {
        return _configuration.GetConfiguration().ResultLimit;
    }

    public bool GetAutoClean()
    {
        return _configuration.GetConfiguration().AutoClean;
    }

    public bool GetCleanOnStartup()
    {
        return _configuration.GetConfiguration().CleanOnStartup;
    }

    public CreateStrategy CreateCollectionStrategy()
    {
        return _configuration.GetConfiguration().CreateCollectionStrategy;
    }

    private string GetDatabaseDescription()
    {
        return $"{_mongoDatabase.Client.Settings.Server.Host}/{_mongoDatabase.DatabaseNamespace.DatabaseName}";
    }

    public void DropDatabase(string name)
    {
        if (GetDatabaseName() != name) throw new InvalidOperationException();
        _mongoClient.DropDatabase(name);
    }

    public IEnumerable<string> GetDatabases()
    {
        //TODO: This uses a call to the database. Or?
        var dbs = _mongoClient.ListDatabases(CancellationToken.None).ToList();
        return dbs.Select(x => x.AsBsonValue["name"].ToString());
    }

    [BsonIgnoreExtraElements]
    private record SizeResult
    {
        [BsonElement("size")]
        public long Size { get; init; }
    }
}