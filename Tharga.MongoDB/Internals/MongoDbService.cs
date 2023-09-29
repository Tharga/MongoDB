using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using MongoDB.Driver.Core.Servers;
using Tharga.MongoDB.Atlas;

namespace Tharga.MongoDB.Internals;

internal class MongoDbService : IMongoDbService
{
    private readonly IRepositoryConfigurationInternal _configuration;
    private readonly IMongoDbFirewallStateService _mongoDbFirewallStateService;
    private readonly MongoUrl _mongoUrl;
    private readonly SemaphoreSlim _mongoClientLock = new(1, 1);
    private MongoClient _mongoClient;
    private readonly SemaphoreSlim _mongoDatabaseLock = new(1, 1);
    private IMongoDatabase _mongoDatabase;

    public MongoDbService(IRepositoryConfigurationInternal configuration, IMongoDbFirewallStateService mongoDbFirewallStateService, ILogger logger)
    {
        _configuration = configuration;
        _mongoDbFirewallStateService = mongoDbFirewallStateService;
        _mongoUrl = configuration.GetDatabaseUrl() ?? throw new NullReferenceException("MongoUrl not found in configuration.");
    }

    private MongoClient GetMongoClient()
    {
        if (_mongoClient != null) return _mongoClient;

        try
        {
            _mongoClientLock.Wait();

            if (_mongoClient != null) return _mongoClient;

            var cfg = MongoClientSettings.FromUrl(_mongoUrl);
            cfg.ConnectTimeout = Debugger.IsAttached ? TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(10);
            cfg.SdamLogFilename = @"C:\temp\Logs\sdam.log";
            cfg.MaxConnectionPoolSize = 1000; //TODO: Try this
            //cfg.WaitQueueTimeout = TimeSpan.FromSeconds(10);
            _mongoClient = new MongoClient(cfg);

            foreach (var server in GetMongoClient().Cluster.Description.Servers)
            {
                var isAlive = (server != null && server.HeartbeatException == null && server.State == ServerState.Connected);
                if (isAlive)
                {
                }
                else
                {
                }
            }

            return _mongoClient;
        }
        finally
        {
            _mongoClientLock.Release();
        }
    }

    private IMongoDatabase GetMongoDatabase()
    {
        if (_mongoDatabase != null) return _mongoDatabase;

        try
        {
            _mongoDatabaseLock.Wait();

            if (_mongoDatabase != null) return _mongoDatabase;

            var settings = new MongoDatabaseSettings { WriteConcern = WriteConcern.WMajority };
            _mongoDatabase = GetMongoClient().GetDatabase(_mongoUrl.DatabaseName, settings);
            return _mongoDatabase;
        }
        finally
        {
            _mongoDatabaseLock.Release();
        }
    }

    public async Task<IMongoCollection<T>> GetCollectionAsync<T>(string collectionName)
    {
        await AssureFirewallAccessAsync();
        return GetMongoDatabase().GetCollection<T>(collectionName);
    }

    public ValueTask AssureFirewallAccessAsync(bool force = false)
    {
        if (_configuration.GetDatabaseUrl().Server.Host.Contains("localhost", StringComparison.InvariantCultureIgnoreCase)) return ValueTask.CompletedTask;
        return _mongoDbFirewallStateService.AssureFirewallAccessAsync(_configuration.GetConfiguration().AccessInfo, force);
    }

    public async ValueTask ResetConnection()
    {
        //TODO: Multiple threads will all try to reset here, do not do it to fast. The locks does not help much in this case.
        try
        {
            await _mongoClientLock.WaitAsync();
            _mongoClient = null;
        }
        finally
        {
            _mongoClientLock.Release();
        }

        try
        {
            await _mongoDatabaseLock.WaitAsync();
            _mongoDatabase = null;
        }
        finally
        {
            _mongoDatabaseLock.Release();
        }
    }

    public string GetDatabaseName()
    {
        return GetMongoDatabase().DatabaseNamespace.DatabaseName;
    }

    public string GetDatabaseAddress()
    {
        return $"{GetMongoClient().Settings.Server.Host}:{GetMongoClient().Settings.Server.Port}";
    }

    public int GetMaxConnectionPoolSize()
    {
        return GetMongoClient().Settings.MaxConnectionPoolSize;
    }

    public string GetDatabaseHostName()
    {
        return GetMongoClient().Settings.Server.Host;
    }

    public Task DropCollectionAsync(string name)
    {
        return GetMongoDatabase().DropCollectionAsync(name);
    }

    public IEnumerable<string> GetCollections()
    {
        return GetMongoDatabase().ListCollections().ToEnumerable().Select(x => x.AsBsonValue["name"].ToString());
    }

    public async IAsyncEnumerable<(string Name, long DocumentCount, long Size)> GetCollectionsWithMetaAsync(string databaseName = null)
    {
        var mongoDatabase = string.IsNullOrEmpty(databaseName) ? GetMongoDatabase() : GetMongoClient().GetDatabase(databaseName);

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
        return GetMongoDatabase().ListCollectionNames(options).Any();
    }

    public long GetSize(string collectionName, IMongoDatabase mongoDatabase = null)
    {
        var size = (mongoDatabase ?? GetMongoDatabase()).RunCommand<SizeResult>($"{{collstats: '{collectionName}'}}").Size;
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
        return $"{GetMongoDatabase().Client.Settings.Server.Host}/{GetMongoDatabase().DatabaseNamespace.DatabaseName}";
    }

    public void DropDatabase(string name)
    {
        if (GetDatabaseName() != name) throw new InvalidOperationException();
        GetMongoClient().DropDatabase(name);
    }

    public IEnumerable<string> GetDatabases()
    {
        var dbs = GetMongoClient().ListDatabases().ToList();
        return dbs.Select(x => x.AsBsonValue["name"].ToString());
    }

    [BsonIgnoreExtraElements]
    private record SizeResult
    {
        [BsonElement("size")]
        public long Size { get; init; }
    }
}