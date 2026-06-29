using Tharga.MongoDB;
using Tharga.TemplateBlazor.Web.Features.Weather;

namespace Tharga.TemplateBlazor.Web.Features.ClusterDemo;

/// <summary>
/// Demo helper that opens real connections against the sample's configurations so the Queue view's
/// per-cluster connection summary has something to show — even though everything points at one local mongod.
///
/// The trick: clusters are identified by their server host(s), pools by host + max pool size. So the three
/// sample configs deliberately differ only in those (and use small pool sizes, set in the connection strings,
/// so a modest burst saturates them):
/// <list type="bullet">
/// <item><b>Core</b> — <c>localhost:27017?maxPoolSize=10</c>.</item>
/// <item><b>Reporting</b> — <c>localhost:27017?maxPoolSize=5</c>: same cluster as Core, smaller pool ⇒ a
/// <i>second pool under the same cluster</i>.</item>
/// <item><b>Archive</b> — <c>127.0.0.1:27017?maxPoolSize=8</c>: same physical server, different host string ⇒ a
/// <i>separate cluster</i>.</item>
/// <item><b>Shared</b> — <c>localhost:27017/Tharga_MongoDB_Shared</c> (fixed db name, no environment token): the
/// ConsoleSample client can point its own "Shared" config at the same database + collection name (<c>ClusterDemo</c>),
/// so that collection shows <i>both</i> the server and the client as sources.</item>
/// </list>
/// Run the ConsoleSample agent alongside (it also targets <c>localhost:27017</c>) to see a second <i>source</i>
/// summed into the localhost cluster.
/// </summary>
public sealed class ClusterConnectionDemo
{
    public static readonly IReadOnlyList<string> Configurations = new[] { "Core", "Reporting", "Archive", "Shared" };

    private readonly ICollectionProvider _collectionProvider;

    public ClusterConnectionDemo(ICollectionProvider collectionProvider)
    {
        _collectionProvider = collectionProvider;
    }

    /// <summary>Fire <paramref name="concurrency"/> concurrent reads at one configuration to open pooled connections.</summary>
    public async Task BurstAsync(string configurationName, int concurrency)
    {
        var tasks = Enumerable.Range(0, concurrency).Select(_ => Task.Run(async () =>
        {
            var collection = _collectionProvider.GetCollection<ILocalWeatherRepositoryCollection, LocalWeatherEntity>(
                new DatabaseContext { ConfigurationName = configurationName, CollectionName = "ClusterDemo" });
            await collection.GetAsync(x => true).ToArrayAsync();
        }));
        await Task.WhenAll(tasks);
    }

    /// <summary>Burst every demo configuration at once.</summary>
    public Task BurstAllAsync(int concurrency) =>
        Task.WhenAll(Configurations.Select(c => BurstAsync(c, concurrency)));
}
