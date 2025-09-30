using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Tharga.MongoDB;

public interface IMongoDbService
{
    Task<IMongoCollection<T>> GetCollectionAsync<T>(string collectionName, TimeSeriesOptions timeSeriesOptions = null);
    string GetDatabaseName();
    string GetDatabaseAddress();
    string GetDatabaseHostName();
    int GetMaxConnectionPoolSize();
    Task DropCollectionAsync(string name);
    IEnumerable<string> GetCollections();
    IAsyncEnumerable<(string Name, long DocumentCount, long Size)> GetCollectionsWithMetaAsync(string databaseName = null);
    Task<bool> DoesCollectionExist(string name);
    void DropDatabase(string name);
    IEnumerable<string> GetDatabases();
    long GetSize(string collectionName, IMongoDatabase mongoDatabase = null);
    Task<DatabaseInfo> GetInfoAsync(bool assureFirewall = true);
    int? GetResultLimit();
    bool GetAutoClean();
    bool GetCleanOnStartup();
    bool DropEmptyCollections();
    ValueTask<string> AssureFirewallAccessAsync(bool force = false);
    LogLevel GetExecuteInfoLogLevel();
    bool ShouldAssureIndex();
}