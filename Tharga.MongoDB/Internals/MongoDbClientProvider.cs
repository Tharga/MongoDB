using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using MongoDB.Driver;

namespace Tharga.MongoDB.Internals;

internal class MongoDbClientProvider : IMongoDbClientProvider
{
    private readonly ConcurrentDictionary<string, Lazy<MongoClient>> _cache = new();

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
                return new MongoClient(settings);
            }, LazyThreadSafetyMode.ExecutionAndPublication)
        );

        return lazyClient.Value;
    }

    string GetServerKey(MongoUrl url)
    {
        return string.Join(",", url.Servers.Select(s => s.ToString()).OrderBy(x => x));
    }
}