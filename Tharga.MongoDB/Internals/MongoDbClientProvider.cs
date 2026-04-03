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

    public MongoDbClientProvider(CommandMonitorService commandMonitor = null)
    {
        _commandMonitor = commandMonitor;
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

                if (_commandMonitor != null)
                {
                    settings.ClusterConfigurator = cb =>
                    {
                        cb.Subscribe<CommandSucceededEvent>(e => _commandMonitor.OnCommandSucceeded(e));
                        cb.Subscribe<CommandFailedEvent>(e => _commandMonitor.OnCommandFailed(e));
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