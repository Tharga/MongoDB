using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB.Internals;

/// <summary>
/// Stores per-cluster config in a <c>_monitorConfig</c> collection in the default configuration's database
/// (a small central registry on the monitor server). Cached in memory; writes upsert and refresh the cache.
/// Resilient: an unreachable database degrades to an empty cache rather than throwing.
/// </summary>
internal sealed class MongoDbClusterConfigStore : IClusterConfigStore
{
    private const string CollectionName = "_monitorConfig";

    private readonly IMongoDbServiceFactory _factory;
    private readonly DatabaseOptions _options;
    private readonly ILogger<MongoDbClusterConfigStore> _logger;
    private readonly ConcurrentDictionary<string, ClusterConfigEntry> _cache = new();
    private int _loaded;

    public MongoDbClusterConfigStore(IMongoDbServiceFactory factory, IOptions<DatabaseOptions> options, ILogger<MongoDbClusterConfigStore> logger)
    {
        _factory = factory;
        _options = options.Value;
        _logger = logger;
    }

    public IReadOnlyList<ClusterConfigEntry> GetAll()
    {
        EnsureLoaded();
        return _cache.Values.OrderBy(x => x.Cluster, StringComparer.Ordinal).ToArray();
    }

    public ClusterConfigEntry Get(string cluster)
    {
        EnsureLoaded();
        return cluster != null && _cache.TryGetValue(cluster, out var entry) ? entry : null;
    }

    public int? GetEffectiveLimit(string cluster) => Get(cluster)?.EffectiveLimit;

    public async Task SetAsync(ClusterConfigEntry entry)
    {
        if (string.IsNullOrEmpty(entry?.Cluster)) return;

        _cache[entry.Cluster] = entry;

        var col = GetCollection();
        if (col == null) return;
        try
        {
            await col.ReplaceOneAsync(new BsonDocument("_id", entry.Cluster), ToBson(entry), new ReplaceOptions { IsUpsert = true });
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to persist cluster config for '{Cluster}'. Kept in memory only.", entry.Cluster);
        }
    }

    private void EnsureLoaded()
    {
        if (System.Threading.Interlocked.Exchange(ref _loaded, 1) == 1) return;

        var col = GetCollection();
        if (col == null) return;
        try
        {
            foreach (var doc in col.Find(FilterDefinition<BsonDocument>.Empty).ToList())
            {
                var entry = FromBson(doc);
                if (entry != null) _cache[entry.Cluster] = entry;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load cluster config; starting empty.");
        }
    }

    private IMongoCollection<BsonDocument> GetCollection()
    {
        try
        {
            var svc = _factory.GetMongoDbService(() => new DatabaseContext { ConfigurationName = _options.DefaultConfigurationName });
            svc.AssureFirewallAccessAsync().AsTask().GetAwaiter().GetResult();
            return (svc as IMongoDbServiceInternal)?.BaseMongoDatabase?.GetCollection<BsonDocument>(CollectionName);
        }
        catch
        {
            return null;
        }
    }

    private static BsonDocument ToBson(ClusterConfigEntry e) => new()
    {
        { "_id", e.Cluster },
        { "Alias", e.Alias ?? (BsonValue)BsonNull.Value },
        { "Tier", e.Tier ?? (BsonValue)BsonNull.Value },
        { "Limit", e.Limit.HasValue ? e.Limit.Value : BsonNull.Value },
        { "WarnThreshold", e.WarnThreshold.HasValue ? e.WarnThreshold.Value : BsonNull.Value },
        { "DangerThreshold", e.DangerThreshold.HasValue ? e.DangerThreshold.Value : BsonNull.Value },
    };

    private static ClusterConfigEntry FromBson(BsonDocument d)
    {
        try
        {
            return new ClusterConfigEntry
            {
                Cluster = d["_id"].AsString,
                Alias = Str(d, "Alias"),
                Tier = Str(d, "Tier"),
                Limit = Int(d, "Limit"),
                WarnThreshold = Dbl(d, "WarnThreshold"),
                DangerThreshold = Dbl(d, "DangerThreshold"),
            };
        }
        catch
        {
            return null;
        }
    }

    private static string Str(BsonDocument d, string n) => d.TryGetValue(n, out var v) && !v.IsBsonNull ? v.AsString : null;
    private static int? Int(BsonDocument d, string n) => d.TryGetValue(n, out var v) && !v.IsBsonNull ? v.ToInt32() : null;
    private static double? Dbl(BsonDocument d, string n) => d.TryGetValue(n, out var v) && !v.IsBsonNull ? v.ToDouble() : null;
}
