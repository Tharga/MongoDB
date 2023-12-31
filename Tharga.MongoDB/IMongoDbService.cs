﻿using System.Collections.Generic;
using System.Threading.Tasks;
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
    Task<DatabaseInfo> GetInfoAsync();
    int? GetResultLimit();
    bool GetAutoClean();
    bool GetCleanOnStartup();
    bool DropEmptyCollections();
    ValueTask AssureFirewallAccessAsync(bool force = false);
}