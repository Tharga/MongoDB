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

internal class MongoDbService : IMongoDbServiceInternal
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

    public async IAsyncEnumerable<CollectionMeta> GetCollectionsWithMetaAsync(string databaseName = null, string collectionNameFilter = null, bool includeDetails = true)
    {
        var mongoDatabase = string.IsNullOrEmpty(databaseName) ? _mongoDatabase : _mongoClient.GetDatabase(databaseName);
        ListCollectionsOptions options = null;
        if (!string.IsNullOrEmpty(collectionNameFilter))
            options = new ListCollectionsOptions { Filter = Builders<BsonDocument>.Filter.Eq("name", collectionNameFilter) };
        IAsyncCursor<BsonDocument> collectionsCursor;
        try
        {
            collectionsCursor = await mongoDatabase.ListCollectionsAsync(options);
        }
        catch (MongoWaitQueueFullException e)
        {
            e.Data["Configuration"] = _configuration.GetConfigurationName();
            e.Data["Database"] = mongoDatabase.DatabaseNamespace.DatabaseName;
            throw;
        }
        var collections = collectionsCursor.ToEnumerable();

        var dbName = mongoDatabase.DatabaseNamespace.DatabaseName;
        var server = ToServerName(dbName);

        foreach (var collection in collections)
        {
            var collectionName = collection["name"].AsString;

            if (!includeDetails)
            {
                yield return new CollectionMeta
                {
                    ConfigurationName = _configuration.GetConfigurationName(),
                    DatabaseName = dbName,
                    CollectionName = collectionName,
                    Server = server,
                    DocumentCount = 0,
                    Size = 0,
                    Types = [],
                    Indexes = [],
                };
                continue;
            }

            var mongoCollection = mongoDatabase.GetCollection<BsonDocument>(collectionName);

            var typesTask = mongoCollection
                .Distinct<string>("_t", FilterDefinition<BsonDocument>.Empty)
                .ToListAsync();
            var statsTask = mongoDatabase.RunCommandAsync<SizeResult>($"{{collstats: '{collectionName}'}}");
            var indexTask = BuildIndicesModel(mongoCollection);

            await Task.WhenAll(typesTask, statsTask, indexTask);

            yield return new CollectionMeta
            {
                ConfigurationName = _configuration.GetConfigurationName(),
                DatabaseName = dbName,
                CollectionName = collectionName,
                Server = server,
                DocumentCount = statsTask.Result.Count,
                Size = statsTask.Result.Size,
                Types = typesTask.Result.ToArray(),
                Indexes = indexTask.Result
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

            var fields = keyDoc.Elements.Select(e => $"{e.Name}_{e.Value}").ToArray();

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

    public async Task<CleanInfo> ReadCleanInfoAsync(string databaseName, string collectionName)
    {
        var mongoDatabase = string.IsNullOrEmpty(databaseName) ? _mongoDatabase : _mongoClient.GetDatabase(databaseName);
        var cleanCollection = mongoDatabase.GetCollection<BsonDocument>("_clean");
        var filter = new BsonDocument("_id", collectionName);
        var doc = await cleanCollection.Find(filter).SingleOrDefaultAsync();
        if (doc == null) return null;

        return new CleanInfo
        {
            SchemaFingerprint = doc.GetValue("SchemaFingerprint", "").AsString,
            CleanedAt = doc.GetValue("CleanedAt", DateTime.MinValue).ToUniversalTime(),
            DocumentsCleaned = doc.GetValue("DocumentsCleaned", 0).AsInt32
        };
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

    public int? GetFetchSize()
    {
        return _configuration.GetConfiguration().FetchSize;
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
        var dbs = _mongoClient.ListDatabases(CancellationToken.None).ToList();
        return dbs.Select(x => x.AsBsonValue["name"].ToString());
    }

    public async Task<MonitorRecord> ReadMonitorRecordAsync(string databaseName, string collectionName)
    {
        var mongoDatabase = string.IsNullOrEmpty(databaseName) ? _mongoDatabase : _mongoClient.GetDatabase(databaseName);
        var col = mongoDatabase.GetCollection<BsonDocument>("_monitor");
        var doc = await col.Find(new BsonDocument("_id", collectionName)).SingleOrDefaultAsync();
        return doc == null ? null : BsonToMonitorRecord(doc);
    }

    public async Task<IEnumerable<MonitorRecord>> GetAllMonitorRecordsAsync(string databaseName)
    {
        var mongoDatabase = string.IsNullOrEmpty(databaseName) ? _mongoDatabase : _mongoClient.GetDatabase(databaseName);
        var col = mongoDatabase.GetCollection<BsonDocument>("_monitor");
        var docs = await col.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
        return docs.Select(BsonToMonitorRecord).ToArray();
    }

    public async Task SaveMonitorRecordAsync(string databaseName, MonitorRecord record)
    {
        var mongoDatabase = string.IsNullOrEmpty(databaseName) ? _mongoDatabase : _mongoClient.GetDatabase(databaseName);
        var col = mongoDatabase.GetCollection<BsonDocument>("_monitor");
        var doc = MonitorRecordToBson(record);
        await col.ReplaceOneAsync(new BsonDocument("_id", record.CollectionName), doc, new ReplaceOptions { IsUpsert = true });
    }

    public async Task RemoveMonitorRecordAsync(string databaseName, string collectionName)
    {
        var mongoDatabase = string.IsNullOrEmpty(databaseName) ? _mongoDatabase : _mongoClient.GetDatabase(databaseName);
        var col = mongoDatabase.GetCollection<BsonDocument>("_monitor");
        await col.DeleteOneAsync(new BsonDocument("_id", collectionName));
    }

    private static BsonValue ToBson(string value) => value != null ? (BsonValue)value : BsonNull.Value;

    private static string BsonStr(BsonValue value) => value == null || value.IsBsonNull ? null : value.AsString;

    private static BsonArray IndexesToBson(IndexMeta[] indexes)
    {
        var arr = new BsonArray();
        if (indexes == null) return arr;
        foreach (var idx in indexes)
            arr.Add(new BsonDocument { { "Name", idx.Name }, { "Fields", new BsonArray(idx.Fields) }, { "IsUnique", idx.IsUnique } });
        return arr;
    }

    private static IndexMeta[] BsonToIndexes(BsonValue val)
    {
        if (val == null || val.IsBsonNull || val is not BsonArray arr) return null;
        return arr.Select(item =>
        {
            var d = item.AsBsonDocument;
            return new IndexMeta
            {
                Name = BsonStr(d.GetValue("Name", BsonNull.Value)),
                Fields = d["Fields"].AsBsonArray.Select(f => f.AsString).ToArray(),
                IsUnique = d.GetValue("IsUnique", false).AsBoolean
            };
        }).ToArray();
    }

    private static BsonDocument MonitorRecordToBson(MonitorRecord r)
    {
        return new BsonDocument
        {
            { "_id", r.CollectionName },
            { "ConfigurationName", ToBson(r.ConfigurationName) },
            { "DatabaseName", ToBson(r.DatabaseName) },
            { "Server", ToBson(r.Server) },
            { "DatabasePart", ToBson(r.DatabasePart) },
            { "Source", r.Source },
            { "Registration", r.Registration },
            { "Types", new BsonArray(r.Types ?? []) },
            { "CollectionTypeName", ToBson(r.CollectionTypeName) },
            { "DocumentCount", r.DocumentCount },
            { "Size", r.Size },
            { "AccessCount", r.AccessCount },
            { "CallCount", r.CallCount },
            { "CurrentIndexes", IndexesToBson(r.CurrentIndexes) }
        };
    }

    private static MonitorRecord BsonToMonitorRecord(BsonDocument doc)
    {
        return new MonitorRecord
        {
            CollectionName = doc["_id"].AsString,
            ConfigurationName = BsonStr(doc.GetValue("ConfigurationName", BsonNull.Value)),
            DatabaseName = BsonStr(doc.GetValue("DatabaseName", BsonNull.Value)),
            Server = BsonStr(doc.GetValue("Server", BsonNull.Value)),
            DatabasePart = BsonStr(doc.GetValue("DatabasePart", BsonNull.Value)),
            Source = doc.GetValue("Source", 0).ToInt32(),
            Registration = doc.GetValue("Registration", 0).ToInt32(),
            Types = doc.GetValue("Types", new BsonArray()) is BsonArray ta ? ta.Select(x => x.AsString).ToArray() : [],
            CollectionTypeName = BsonStr(doc.GetValue("CollectionTypeName", BsonNull.Value)),
            DocumentCount = doc.GetValue("DocumentCount", 0L).ToInt64(),
            Size = doc.GetValue("Size", 0L).ToInt64(),
            AccessCount = doc.GetValue("AccessCount", 0).ToInt32(),
            CallCount = doc.GetValue("CallCount", 0).ToInt32(),
            CurrentIndexes = BsonToIndexes(doc.GetValue("CurrentIndexes", BsonNull.Value))
        };
    }

    [BsonIgnoreExtraElements]
    // ReSharper disable once ClassNeverInstantiated.Local
    private record SizeResult
    {
        [BsonElement("size")]
        public long Size { get; init; }

        [BsonElement("count")]
        public long Count { get; init; }
    }
}