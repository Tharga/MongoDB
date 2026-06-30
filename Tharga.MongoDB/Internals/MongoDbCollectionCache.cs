using Microsoft.Extensions.Logging;
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
    private readonly ILogger<MongoDbCollectionCache> _logger;
    private readonly ConcurrentDictionary<string, CollectionInfo> _dict = new();

    public MongoDbCollectionCache(IMongoDbServiceFactory factory, IRepositoryConfiguration repositoryConfiguration, IOptions<DatabaseOptions> options, ILogger<MongoDbCollectionCache> logger)
    {
        _factory = factory;
        _repositoryConfiguration = repositoryConfiguration;
        _options = options.Value;
        _logger = logger;
    }

    public bool TryGet(string key, out CollectionInfo value) => _dict.TryGetValue(key, out value);

    public CollectionInfo AddOrUpdate(string key, Func<string, CollectionInfo> addFactory, Func<string, CollectionInfo, CollectionInfo> updateFactory)
        => _dict.AddOrUpdate(key, addValueFactory: addFactory, updateValueFactory: updateFactory);

    public bool TryRemove(string key, out CollectionInfo value) => _dict.TryRemove(key, out value);

    public void Set(string key, CollectionInfo value) => _dict[key] = value;

    public IEnumerable<CollectionInfo> GetAll() => _dict.Values;

    public IEnumerable<string> GetKeys() => _dict.Keys;

    public void Clear() => _dict.Clear();

    public async Task LoadAsync()
    {
        var databaseConfigurationNames = _repositoryConfiguration.GetDatabaseConfigurationNames().ToArray();
        foreach (var configName in databaseConfigurationNames)
        {
            var db = GetBaseDatabase(configName);
            if (db == null) continue;

            var col = db.GetCollection<BsonDocument>("_monitor");
            List<BsonDocument> docs;
            try
            {
                docs = await col.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read _monitor collection for config '{ConfigName}'. Dropping and starting fresh.", configName);
                await TryDropMonitorAsync(col, configName);
                continue;
            }

            var hadDeserializationError = false;
            var loadedKeys = new List<string>();
            foreach (var doc in docs)
            {
                var info = BsonToCollectionInfo(doc, configName);
                if (info == null)
                {
                    hadDeserializationError = true;
                    continue;
                }
                info.Discovery |= Discovery.Monitor;
                _dict[info.Key] = info;
                loadedKeys.Add(info.Key);
            }

            if (hadDeserializationError)
            {
                _logger.LogWarning("One or more _monitor documents failed to deserialize for config '{ConfigName}'. Dropping collection and starting fresh.", configName);
                if (await TryDropMonitorAsync(col, configName))
                {
                    foreach (var key in loadedKeys)
                        _dict.TryRemove(key, out _);
                }
            }
        }
    }

    /// <summary>
    /// Drops the _monitor collection as part of the "start fresh" recovery. The drop is itself a
    /// write against a database that may be unreachable (the very reason we are recovering), so a
    /// failure here must never propagate — it would abort process startup. Returns true when the
    /// drop succeeded.
    /// </summary>
    private async Task<bool> TryDropMonitorAsync(IMongoCollection<BsonDocument> col, string configName)
    {
        try
        {
            await col.Database.DropCollectionAsync("_monitor");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to drop _monitor collection for config '{ConfigName}' during recovery. Continuing without the persisted cache for this configuration.", configName);
            return false;
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
            var svc = _factory.GetMongoDbService(() => new DatabaseContext { ConfigurationName = configName ?? _options.DefaultConfigurationName });
            svc.AssureFirewallAccessAsync().AsTask().GetAwaiter().GetResult();
            return (svc as IMongoDbServiceInternal)?.BaseMongoDatabase;
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

    internal static BsonValue CleanToBson(CleanInfo clean)
    {
        if (clean == null) return BsonNull.Value;
        return new BsonDocument
        {
            { "SchemaFingerprint", ToBson(clean.SchemaFingerprint) },
            { "CleanedAt", clean.CleanedAt },
            { "DocumentsCleaned", clean.DocumentsCleaned },
        };
    }

    internal static CleanInfo BsonToClean(BsonValue val)
    {
        if (val == null || val.IsBsonNull || val is not BsonDocument d) return null;
        return new CleanInfo
        {
            SchemaFingerprint = BsonStr(d.GetValue("SchemaFingerprint", BsonNull.Value)),
            CleanedAt = d.GetValue("CleanedAt", BsonNull.Value) is { IsBsonNull: false } c ? c.ToUniversalTime() : default,
            DocumentsCleaned = d.GetValue("DocumentsCleaned", 0).ToInt32(),
        };
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
            { "Source", (int)info.Discovery },
            { "Registration", (int)info.Registration },
            { "Types", new BsonArray(info.EntityTypes ?? []) },
            { "CollectionTypeName", ToBson(info.CollectionType?.AssemblyQualifiedName) },
            { "CollectionTypeDisplayName", ToBson(info.CollectionType?.Name ?? info.CollectionTypeName) },
            { "DocumentCount", info.Stats?.DocumentCount ?? 0L },
            { "Size", info.Stats?.Size ?? 0L },
            { "CurrentIndexes", IndexesToBson(info.Index?.Current) },
            { "DefinedIndexes", IndexesToBson(info.Index?.Defined) },
            { "StatsUpdatedAt", info.Stats?.UpdatedAt.HasValue == true ? (BsonValue)info.Stats.UpdatedAt.Value : BsonNull.Value },
            { "IndexUpdatedAt", info.Index?.UpdatedAt.HasValue == true ? (BsonValue)info.Index.UpdatedAt.Value : BsonNull.Value },
            { "Clean", CleanToBson(info.Clean) },
            { "CurrentSchemaFingerprint", ToBson(info.CurrentSchemaFingerprint) },
            { "ReportedAt", info.ReportedAt.HasValue ? (BsonValue)info.ReportedAt.Value : BsonNull.Value },
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
            var collectionTypeDisplayName = BsonStr(doc.GetValue("CollectionTypeDisplayName", BsonNull.Value)) ?? collectionType?.Name;

            var statsUpdatedAt = doc.TryGetValue("StatsUpdatedAt", out var suVal) && !suVal.IsBsonNull
                ? (DateTime?)suVal.ToUniversalTime()
                : null;
            var indexUpdatedAt = doc.TryGetValue("IndexUpdatedAt", out var iuVal) && !iuVal.IsBsonNull
                ? (DateTime?)iuVal.ToUniversalTime()
                : null;
            var reportedAt = doc.TryGetValue("ReportedAt", out var raVal) && !raVal.IsBsonNull
                ? (DateTime?)raVal.ToUniversalTime()
                : null;

            var documentCount = doc.GetValue("DocumentCount", 0L).ToInt64();
            var size = doc.GetValue("Size", 0L).ToInt64();
            var currentIndexes = BsonToIndexes(doc.GetValue("CurrentIndexes", BsonNull.Value));
            var definedIndexes = BsonToIndexes(doc.GetValue("DefinedIndexes", BsonNull.Value));
            var clean = BsonToClean(doc.GetValue("Clean", BsonNull.Value));
            var currentSchemaFingerprint = BsonStr(doc.GetValue("CurrentSchemaFingerprint", BsonNull.Value));

            return new CollectionInfo
            {
                ConfigurationName = configName,
                DatabaseName = databaseName,
                CollectionName = collectionName,
                Server = BsonStr(doc.GetValue("Server", BsonNull.Value)) ?? string.Empty,
                DatabasePart = BsonStr(doc.GetValue("DatabasePart", BsonNull.Value)),
                Discovery = (Discovery)doc.GetValue("Source", 0).ToInt32(),
                Registration = registration,
                EntityTypes = doc.GetValue("Types", new BsonArray()) is BsonArray ta
                    ? ta.Select(x => x.IsString ? x.AsString : null).ToArray()
                    : [],
                CollectionType = collectionType,
                CollectionTypeName = collectionTypeDisplayName,
                Stats = documentCount > 0 || size > 0 || statsUpdatedAt.HasValue
                    ? new CollectionStats { DocumentCount = documentCount, Size = size, UpdatedAt = statsUpdatedAt }
                    : null,
                Index = currentIndexes != null || definedIndexes != null
                    ? new IndexInfo { Current = currentIndexes, Defined = definedIndexes ?? [], UpdatedAt = indexUpdatedAt }
                    : null,
                Clean = clean,
                CurrentSchemaFingerprint = currentSchemaFingerprint,
                ReportedAt = reportedAt,
            };
        }
        catch
        {
            return null;
        }
    }
}
