using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Collections.Generic;
using System.Threading.Tasks;
using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB;

public interface IMongoDbService
{
    Task<IMongoCollection<T>> GetCollectionAsync<T>(string name);

    /// <summary>
    /// Returns the raw <see cref="IMongoCollection{BsonDocument}"/> for an arbitrary database name.
    /// Used by document-inspection paths that operate on per-tenant databases (DatabasePart) without
    /// rebuilding the whole <see cref="IMongoDbService"/> instance per database.
    /// </summary>
    Task<IMongoCollection<BsonDocument>> GetCollectionAsync(string databaseName, string collectionName);

    Task<IMongoCollection<T>> CreateCollectionAsync<T>(string name);
    string GetConfigurationName();
    string GetDatabaseName();
    string GetDatabaseAddress();
    string GetDatabaseHostName();
    int GetMaxConnectionPoolSize();
    string GetServerKey();
    Task DropCollectionAsync(string name);
    IEnumerable<string> GetCollections();
    IAsyncEnumerable<CollectionMeta> GetCollectionsWithMetaAsync(string databaseName = null, string collectionNameFilter = null, bool includeDetails = true);
    Task<bool> DoesCollectionExist(string name);
    Task<CleanInfo> ReadCleanInfoAsync(string databaseName, string collectionName);
    Task<Dictionary<string, CleanInfo>> ReadAllCleanInfoAsync(string databaseName);
    void DropDatabase(string name);
    IEnumerable<string> GetDatabases();
    long GetSize(string collectionName, IMongoDatabase mongoDatabase = null);
    Task<DatabaseInfo> GetInfoAsync(bool assureFirewall = true);
    int? GetFetchSize();
    bool GetAutoClean();
    bool GetCleanOnStartup();
    CreateStrategy CreateCollectionStrategy();
    ValueTask<string> AssureFirewallAccessAsync(bool force = false);
    AssureIndexMode GetAssureIndexMode();
}