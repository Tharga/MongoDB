using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
    private readonly MongoClient _mongoClient;
    private readonly IMongoDatabase _mongoDatabase;

    public MongoDbService(IRepositoryConfigurationInternal configuration, IMongoDbFirewallStateService mongoDbFirewallStateService, ILogger logger)
    {
        _configuration = configuration;
        _mongoDbFirewallStateService = mongoDbFirewallStateService;
        var mongoUrl = configuration.GetDatabaseUrl() ?? throw new NullReferenceException("MongoUrl not found in configuration.");
        //_mongoClient = new MongoClient(mongoUrl);
        var cfg = MongoClientSettings.FromUrl(mongoUrl);
        //TODO: Make timeout configurable
        cfg.ConnectTimeout = Debugger.IsAttached ? TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(10);
        _mongoClient = new MongoClient(cfg);
        var settings = new MongoDatabaseSettings { WriteConcern = WriteConcern.WMajority };
        _mongoDatabase = _mongoClient.GetDatabase(mongoUrl.DatabaseName, settings);
    }

    public async Task<IMongoCollection<T>> GetCollectionAsync<T>(string collectionName)
    {
        await AssureFirewallAccessAsync();
        return _mongoDatabase.GetCollection<T>(collectionName);
    }

    public ValueTask AssureFirewallAccessAsync(bool force = false)
    {
        if (_configuration.GetDatabaseUrl().Server.Host.Contains("localhost", StringComparison.InvariantCultureIgnoreCase)) return ValueTask.CompletedTask;
        return _mongoDbFirewallStateService.AssureFirewallAccessAsync(_configuration.GetConfiguration().AccessInfo, force);
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

    public async IAsyncEnumerable<(string Name, long DocumentCount, long Size)> GetCollectionsWithMetaAsync(string databaseName = null)
    {
        var mongoDatabase = string.IsNullOrEmpty(databaseName) ? _mongoDatabase : _mongoClient.GetDatabase(databaseName);

        var collections = (await mongoDatabase.ListCollectionsAsync()).ToEnumerable();

        foreach (var collection in collections)
        {
            var name = collection.AsBsonValue["name"].ToString();
            var documents = await mongoDatabase.GetCollection<object>(name).CountDocumentsAsync(y => true);
            var size = GetSize(name, mongoDatabase);
            yield return (name, documents, size);
        }
    }

    public bool DoesCollectionExist(string name)
    {
        var filter = new BsonDocument("name", name);
        var options = new ListCollectionNamesOptions { Filter = filter };
        return _mongoDatabase.ListCollectionNames(options).Any();
    }

    public long GetSize(string collectionName, IMongoDatabase mongoDatabase = null)
    {
        var size = (mongoDatabase ?? _mongoDatabase).RunCommand<SizeResult>($"{{collstats: '{collectionName}'}}").Size;
        return size;
    }

    public Task<DatabaseInfo> GetInfoAsync()
    {
        try
        {
            return Task.FromResult(new DatabaseInfo
            {
                CanConnect = true,
                Message = GetDatabaseDescription(),
                CollectionCount = GetCollections().Count()
            });
        }
        catch (Exception e)
        {
            return Task.FromResult(new DatabaseInfo
            {
                Message = e.Message
            });
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
        var dbs = _mongoClient.ListDatabases().ToList();
        return dbs.Select(x => x.AsBsonValue["name"].ToString());
    }

    [BsonIgnoreExtraElements]
    private record SizeResult
    {
        [BsonElement("size")]
        public long Size { get; init; }
    }
}