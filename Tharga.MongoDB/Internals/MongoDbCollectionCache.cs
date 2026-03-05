using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB.Internals;

internal class MongoDbCollectionCache : ICollectionCache
{
    private readonly IMongoDbServiceFactory _factory;
    private readonly IRepositoryConfiguration _repositoryConfiguration;
    private readonly DatabaseOptions _options;
    private readonly ConcurrentDictionary<string, CollectionInfo> _dict = new();

    public MongoDbCollectionCache(IMongoDbServiceFactory factory, IRepositoryConfiguration repositoryConfiguration, IOptions<DatabaseOptions> options)
    {
        _factory = factory;
        _repositoryConfiguration = repositoryConfiguration;
        _options = options.Value;
    }

    public bool TryGet(string key, out CollectionInfo value) => _dict.TryGetValue(key, out value);

    public IEnumerable<CollectionInfo> GetAll() => _dict.Values;

    public CollectionInfo AddOrUpdate(string key,
        Func<string, CollectionInfo> addValueFactory,
        Func<string, CollectionInfo, CollectionInfo> updateValueFactory)
        => _dict.AddOrUpdate(key, addValueFactory, updateValueFactory);

    public bool Remove(string key, out CollectionInfo value) => _dict.TryRemove(key, out value);

    public async Task InitializeAsync()
    {
        foreach (var configName in _repositoryConfiguration.GetDatabaseConfigurationNames())
        {
            var db = GetBaseDatabase(configName);
            if (db == null) continue;

            var col = db.GetCollection<BsonDocument>("_monitor");
            var docs = await col.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
            foreach (var doc in docs)
            {
                var info = BsonToCollectionInfo(doc, configName);
                if (info == null) continue;
                info.Source |= Source.Monitor;
                _dict[info.Key] = info;
            }
        }
    }

    public async Task SaveAsync(CollectionInfo info)
    {
        var db = GetBaseDatabase(info.ConfigurationName?.Value);
        if (db == null) return;

        var col = db.GetCollection<BsonDocument>("_monitor");
        var id = MonitorKey(info.DatabaseName, info.CollectionName);
        var doc = CollectionInfoToBson(id, info);
        await col.ReplaceOneAsync(new BsonDocument("_id", id), doc, new ReplaceOptions { IsUpsert = true });
    }

    public async Task DeleteAsync(string databaseName, string collectionName)
    {
        var id = MonitorKey(databaseName, collectionName);
        foreach (var configName in _repositoryConfiguration.GetDatabaseConfigurationNames())
        {
            var db = GetBaseDatabase(configName);
            if (db == null) continue;
            var col = db.GetCollection<BsonDocument>("_monitor");
            await col.DeleteOneAsync(new BsonDocument("_id", id));
        }
    }

    public async Task ResetAsync()
    {
        _dict.Clear();
        foreach (var configName in _repositoryConfiguration.GetDatabaseConfigurationNames())
        {
            var db = GetBaseDatabase(configName);
            if (db == null) continue;
            var col = db.GetCollection<BsonDocument>("_monitor");
            await col.DeleteManyAsync(FilterDefinition<BsonDocument>.Empty);
        }
    }

    private IMongoDatabase GetBaseDatabase(string configName)
    {
        try
        {
            var svc = _factory.GetMongoDbService(() => new DatabaseContext { ConfigurationName = configName ?? _options.DefaultConfigurationName }) as IMongoDbServiceInternal;
            return svc?.BaseMongoDatabase;
        }
        catch
        {
            return null;
        }
    }

    internal static string MonitorKey(string databaseName, string collectionName) => $"{databaseName}/{collectionName}";

    internal static BsonValue ToBson(string value) => value != null ? (BsonValue)value : BsonNull.Value;

    internal static string BsonStr(BsonValue value) => value == null || value.IsBsonNull ? null : value.AsString;

    internal static BsonArray IndexesToBson(IndexMeta[] indexes)
    {
        var arr = new BsonArray();
        if (indexes == null) return arr;
        foreach (var idx in indexes)
            arr.Add(new BsonDocument { { "Name", idx.Name }, { "Fields", new BsonArray(idx.Fields) }, { "IsUnique", idx.IsUnique } });
        return arr;
    }

    internal static IndexMeta[] BsonToIndexes(BsonValue val)
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

    internal static BsonDocument CollectionInfoToBson(string id, CollectionInfo info)
    {
        return new BsonDocument
        {
            { "_id", id },
            { "CollectionName", info.CollectionName },
            { "ConfigurationName", ToBson(info.ConfigurationName?.Value) },
            { "DatabaseName", ToBson(info.DatabaseName) },
            { "Server", ToBson(info.Server) },
            { "DatabasePart", ToBson(info.DatabasePart) },
            { "Source", (int)info.Source },
            { "Registration", (int)info.Registration },
            { "Types", new BsonArray(info.Types ?? []) },
            { "CollectionTypeName", ToBson(info.CollectionType?.AssemblyQualifiedName) },
            { "DocumentCount", info.DocumentCount?.Count ?? 0L },
            { "Size", info.Size },
            { "CallCount", info.CallCount },
            { "CurrentIndexes", IndexesToBson(info.Index?.Current) },
            { "StatsUpdatedAt", info.StatsUpdatedAt.HasValue ? (BsonValue)info.StatsUpdatedAt.Value : BsonNull.Value },
            { "IndexUpdatedAt", info.IndexUpdatedAt.HasValue ? (BsonValue)info.IndexUpdatedAt.Value : BsonNull.Value },
        };
    }

    internal static CollectionInfo BsonToCollectionInfo(BsonDocument doc, string configName)
    {
        try
        {
            var id = doc["_id"].AsString;
            // Support both new composite format ("databaseName/collectionName") and legacy format ("collectionName")
            var collectionName = doc.TryGetValue("CollectionName", out var cnVal) && !cnVal.IsBsonNull
                ? cnVal.AsString
                : (id.Contains('/') ? id[(id.LastIndexOf('/') + 1)..] : id);
            var databaseName = BsonStr(doc.GetValue("DatabaseName", BsonNull.Value));
            if (string.IsNullOrEmpty(databaseName)) return null;

            var registration = (Registration)doc.GetValue("Registration", 0).ToInt32();
            var collectionTypeName = BsonStr(doc.GetValue("CollectionTypeName", BsonNull.Value));
            var collectionType = collectionTypeName != null ? Type.GetType(collectionTypeName) : null;

            var statsUpdatedAt = doc.TryGetValue("StatsUpdatedAt", out var suVal) && !suVal.IsBsonNull
                ? (DateTime?)suVal.ToUniversalTime()
                : null;
            var indexUpdatedAt = doc.TryGetValue("IndexUpdatedAt", out var iuVal) && !iuVal.IsBsonNull
                ? (DateTime?)iuVal.ToUniversalTime()
                : null;

            var currentIndexes = BsonToIndexes(doc.GetValue("CurrentIndexes", BsonNull.Value));

            return new CollectionInfo
            {
                ConfigurationName = configName,
                DatabaseName = databaseName,
                CollectionName = collectionName,
                Server = BsonStr(doc.GetValue("Server", BsonNull.Value)) ?? string.Empty,
                DatabasePart = BsonStr(doc.GetValue("DatabasePart", BsonNull.Value)),
                Source = (Source)doc.GetValue("Source", 0).ToInt32(),
                Registration = registration,
                Types = doc.GetValue("Types", new BsonArray()) is BsonArray ta
                    ? ta.Select(x => x.IsString ? x.AsString : null).ToArray()
                    : [],
                CollectionType = collectionType,
                DocumentCount = doc.GetValue("DocumentCount", 0L).ToInt64() is long dc and > 0
                    ? new DocumentCount { Count = dc }
                    : null,
                Size = doc.GetValue("Size", 0L).ToInt64(),
                CallCount = doc.GetValue("CallCount", 0).ToInt32(),
                Index = currentIndexes != null ? new IndexInfo { Current = currentIndexes, Defined = [] } : null,
                StatsUpdatedAt = statsUpdatedAt,
                IndexUpdatedAt = indexUpdatedAt,
            };
        }
        catch
        {
            return null;
        }
    }
}
