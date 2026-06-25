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
        return string.Join(",", url.Servers.Select(s => s.ToString()).OrderBy(x => x));
    }
}