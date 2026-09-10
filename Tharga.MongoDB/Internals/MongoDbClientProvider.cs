using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using MongoDB.Driver;
using MongoDB.Driver.Core.Configuration;
using MongoDB.Driver.Core.Events;

namespace Tharga.MongoDB.Internals;

internal enum ClusterConfiguratorStepKind
{
    Existing,
    BuiltIn,
    Consumer
}

internal readonly record struct ClusterConfiguratorStep(ClusterConfiguratorStepKind Kind, Action<ClusterBuilder> Configure);

internal class MongoDbClientProvider : IMongoDbClientProvider
{
    private readonly ConcurrentDictionary<string, Lazy<MongoClient>> _cache = new();
    private readonly CommandMonitorService _commandMonitor;
    private readonly IConnectionPoolMonitor _connectionPoolMonitor;
    private readonly IReadOnlyList<Action<ClusterBuilder>> _clusterConfigurators;
    private readonly CommandActivityEmitter _activityEmitter;

    public MongoDbClientProvider(CommandMonitorService commandMonitor = null, IConnectionPoolMonitor connectionPoolMonitor = null, IReadOnlyList<Action<ClusterBuilder>> clusterConfigurators = null, CommandActivityEmitter activityEmitter = null)
    {
        _commandMonitor = commandMonitor;
        _connectionPoolMonitor = connectionPoolMonitor;
        _clusterConfigurators = clusterConfigurators ?? Array.Empty<Action<ClusterBuilder>>();
        _activityEmitter = activityEmitter;
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

                var steps = BuildConfiguratorSteps(settings.ClusterConfigurator, key);
                if (steps.Count > 0)
                {
                    settings.ClusterConfigurator = cb =>
                    {
                        foreach (var step in steps) step.Configure(cb);
                    };
                }

                return new MongoClient(settings);
            }, LazyThreadSafetyMode.ExecutionAndPublication)
        );

        return lazyClient.Value;
    }

    /// <summary>
    /// The cluster-builder callbacks applied to a new client, in the order they run: any configurator
    /// already on the settings, then the built-in monitor subscriptions, then consumer callbacks registered
    /// through <c>DatabaseOptions.ConfigureCluster</c>. Composed rather than assigned, so a consumer hook
    /// cannot silently replace the monitor subscriptions.
    /// </summary>
    internal IReadOnlyList<ClusterConfiguratorStep> BuildConfiguratorSteps(Action<ClusterBuilder> existing, string serverKey)
    {
        var steps = new List<ClusterConfiguratorStep>();

        if (existing != null)
            steps.Add(new ClusterConfiguratorStep(ClusterConfiguratorStepKind.Existing, existing));

        var builtIn = BuildBuiltInStep(serverKey);
        if (builtIn != null)
            steps.Add(new ClusterConfiguratorStep(ClusterConfiguratorStepKind.BuiltIn, builtIn));

        steps.AddRange(_clusterConfigurators.Select(x => new ClusterConfiguratorStep(ClusterConfiguratorStepKind.Consumer, x)));

        return steps;
    }

    private Action<ClusterBuilder> BuildBuiltInStep(string serverKey)
    {
        if (_commandMonitor == null && _connectionPoolMonitor == null && _activityEmitter == null) return null;

        return cb =>
        {
            if (_activityEmitter != null)
            {
                cb.Subscribe<CommandStartedEvent>(e => _activityEmitter.OnCommandStarted(e));
                cb.Subscribe<CommandSucceededEvent>(e => _activityEmitter.OnCommandSucceeded(e));
                cb.Subscribe<CommandFailedEvent>(e => _activityEmitter.OnCommandFailed(e));
            }

            if (_commandMonitor != null)
            {
                cb.Subscribe<CommandSucceededEvent>(e => _commandMonitor.OnCommandSucceeded(e));
                cb.Subscribe<CommandFailedEvent>(e => _commandMonitor.OnCommandFailed(e));
            }

            if (_connectionPoolMonitor != null)
            {
                // Count actual open pooled connections for this cluster (CMAP create/close events).
                cb.Subscribe<ConnectionCreatedEvent>(_ => _connectionPoolMonitor.OnConnectionCreated(serverKey));
                cb.Subscribe<ConnectionClosedEvent>(_ => _connectionPoolMonitor.OnConnectionClosed(serverKey));
            }
        };
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