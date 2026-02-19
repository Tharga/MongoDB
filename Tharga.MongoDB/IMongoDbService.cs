using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using System.Collections.Generic;
using System.Threading.Tasks;
using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB;

public interface IMongoDbService
{
    Task<IMongoCollection<T>> GetCollectionAsync<T>(string name);
    Task<IMongoCollection<T>> CreateCollectionAsync<T>(string name);
    string GetConfigurationName();
    string GetDatabaseName();
    string GetDatabaseAddress();
    string GetDatabaseHostName();
    int GetMaxConnectionPoolSize();
    Task DropCollectionAsync(string name);
    IEnumerable<string> GetCollections();
    IAsyncEnumerable<CollectionMeta> GetCollectionsWithMetaAsync(string databaseName = null, string collectionNameFilter = null, bool includeDetails = true);
    Task<bool> DoesCollectionExist(string name);
    void DropDatabase(string name);
    IEnumerable<string> GetDatabases();
    long GetSize(string collectionName, IMongoDatabase mongoDatabase = null);
    Task<DatabaseInfo> GetInfoAsync(bool assureFirewall = true);
    int? GetFetchSize();
    bool GetAutoClean();
    bool GetCleanOnStartup();
    CreateStrategy CreateCollectionStrategy();
    ValueTask<string> AssureFirewallAccessAsync(bool force = false);
    LogLevel GetExecuteInfoLogLevel();
    AssureIndexMode GetAssureIndexMode();
}