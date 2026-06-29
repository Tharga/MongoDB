using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using MongoDB.Driver;
using MongoDB.Driver.Core.Events;

namespace Tharga.MongoDB.Internals;

internal class MongoDbClientProvider : IMongoDbClientProvider
{
    private readonly ConcurrentDictionary<string, Lazy<MongoClient>> _cache = new();
    private readonly CommandMonitorService _commandMonitor;
    private readonly IConnectionPoolMonitor _connectionPoolMonitor;

    public MongoDbClientProvider(CommandMonitorService commandMonitor = null, IConnectionPoolMonitor connectionPoolMonitor = null)
    {
        _commandMonitor = commandMonitor;
        _connectionPoolMonitor = connectionPoolMonitor;
    }

    public MongoClient GetClient(MongoUrl mongoUrl)
    {
        var key = GetServerKey(mongoUrl);

        var lazyClient = _cache.GetOrAdd(key, _ =>
            new Lazy<MongoClient>(() =>
            {
                var settings = MongoClientSettings.FromUrl(mongoUrl);
                settings.ConnectTimeout = Debugger.IsAttached
                    ? TimeSpan.FromSeconds(5)
                    : TimeSpan.FromSeconds(10);

                _connectionPoolMonitor?.SetMaxPoolSize(key, settings.MaxConnectionPoolSize);

                if (_commandMonitor != null || _connectionPoolMonitor != null)
                {
                    settings.ClusterConfigurator = cb =>
                    {
                        if (_commandMonitor != null)
                        {
                            cb.Subscribe<CommandSucceededEvent>(e => _commandMonitor.OnCommandSucceeded(e));
                            cb.Subscribe<CommandFailedEvent>(e => _commandMonitor.OnCommandFailed(e));
                        }

                        if (_connectionPoolMonitor != null)
                        {
                            // Count actual open pooled connections for this cluster (CMAP create/close events).
                            cb.Subscribe<ConnectionCreatedEvent>(_ => _connectionPoolMonitor.OnConnectionCreated(key));
                            cb.Subscribe<ConnectionClosedEvent>(_ => _connectionPoolMonitor.OnConnectionClosed(key));
                        }
                    };
                }

                return new MongoClient(settings);
            }, LazyThreadSafetyMode.ExecutionAndPublication)
        );

        return lazyClient.Value;
    }

    internal static string GetServerKey(MongoUrl url)
    {
        // MaxConnectionPoolSize is part of the key so two configurations pointing at the same cluster
        // with different pool sizes get their own MongoClient (and their own ExecuteLimiter pool, which
        // shares this key) instead of silently sharing whichever client was created first.
        var servers = string.Join(",", url.Servers.Select(s => s.ToString()).OrderBy(x => x));
        return $"{servers}|pool={url.MaxConnectionPoolSize}";
    }

    private const string PoolSizeSeparator = "|pool=";

    /// <summary>
    /// The cluster identity (the server host(s)) for a server-key — i.e. the key with the <c>|pool=</c> size
    /// suffix removed. Two pools that differ only in max pool size collapse to the same cluster.
    /// </summary>
    internal static string ClusterOf(string serverKey)
    {
        if (string.IsNullOrEmpty(serverKey)) return serverKey;
        var i = serverKey.IndexOf(PoolSizeSeparator, StringComparison.Ordinal);
        return i >= 0 ? serverKey[..i] : serverKey;
    }

    /// <summary>
    /// Best-effort classification of a cluster (host string) as an Atlas deployment from its host name.
    /// Atlas hosts live on <c>mongodb.net</c> (and the gov variant); anything else is treated as self-hosted.
    /// </summary>
    internal static bool IsAtlasCluster(string cluster)
        => !string.IsNullOrEmpty(cluster)
           && (cluster.Contains(".mongodb.net", StringComparison.OrdinalIgnoreCase)
               || cluster.Contains(".mongodbgov.net", StringComparison.OrdinalIgnoreCase));
}