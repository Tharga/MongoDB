﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Tharga.MongoDB.Atlas;

namespace Tharga.MongoDB.Internals;

internal class MongoDbService : IMongoDbService
{
    private readonly IRepositoryConfigurationInternal _configuration;
    private readonly IMongoDbFirewallStateService _mongoDbFirewallStateService;
    private readonly ILogger _logger;
    private readonly MongoClient _mongoClient;
    private readonly IMongoDatabase _mongoDatabase;

    public MongoDbService(IRepositoryConfigurationInternal configuration, IMongoDbFirewallStateService mongoDbFirewallStateService, ILogger logger)
    {
        _configuration = configuration;
        _mongoDbFirewallStateService = mongoDbFirewallStateService;
        _logger = logger;
        var mongoUrl = configuration.GetDatabaseUrl() ?? throw new NullReferenceException("MongoUrl not found in configuration.");
        var cfg = MongoClientSettings.FromUrl(mongoUrl);
        cfg.ConnectTimeout = Debugger.IsAttached ? TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(10);
        _mongoClient = new MongoClient(cfg);
        var settings = new MongoDatabaseSettings { WriteConcern = WriteConcern.WMajority };
        _mongoDatabase = _mongoClient.GetDatabase(mongoUrl.DatabaseName, settings);
    }

    public event EventHandler<CollectionAccessEventArgs> CollectionAccessEvent;

    public async Task<IMongoCollection<T>> GetCollectionAsync<T>(string collectionName/*, TimeSeriesOptions timeSeriesOptions*/)
    {
        await AssureFirewallAccessAsync();

        var collection = _mongoDatabase.GetCollection<T>(collectionName);

        CollectionAccessEvent?.Invoke(this, new CollectionAccessEventArgs(collectionName, typeof(T), collection.GetType()));

        return collection;
    }

    public async ValueTask<string> AssureFirewallAccessAsync(bool force = false)
    {
        if (_configuration.GetDatabaseUrl().Server.Host.Contains("localhost", StringComparison.InvariantCultureIgnoreCase)) return default;
        var message = await _mongoDbFirewallStateService.AssureFirewallAccessAsync(_configuration.GetConfiguration().AccessInfo, force);
        _logger.LogDebug(message);
        return message;
    }

    public LogLevel GetExecuteInfoLogLevel()
    {
        return _configuration?.GetExecuteInfoLogLevel() ?? LogLevel.Debug;
    }

    public bool ShouldAssureIndex()
    {
        return _configuration?.ShouldAssureIndex() ?? true;
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
            var name = collection["name"].AsString;
            var mongoCollection = mongoDatabase.GetCollection<BsonDocument>(name);

            var types = await mongoCollection
                .Distinct<string>("_t", FilterDefinition<BsonDocument>.Empty)
                .ToListAsync();

            var documents = await mongoCollection.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty);
            var size = GetSize(name, mongoDatabase);

            var indexModels = new List<IndexMeta>();
            using var cursor = await mongoCollection.Indexes.ListAsync();
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

            yield return new CollectionMeta
            {
                Name = name,
                DocumentCount = documents,
                Size = size,
                Types = types.ToArray(),
                Indexes = indexModels
                    .Where(x => !x.Name.StartsWith("_id_"))
                    .ToArray()
            };
        }
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

    public bool DropEmptyCollections()
    {
        return _configuration.GetConfiguration().DropEmptyCollections;
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

    [BsonIgnoreExtraElements]
    private record SizeResult
    {
        [BsonElement("size")]
        public long Size { get; init; }
    }
}