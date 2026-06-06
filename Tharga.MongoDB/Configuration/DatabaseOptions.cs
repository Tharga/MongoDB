using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Tharga.MongoDB.Internals;

namespace Tharga.MongoDB.Configuration;

/// <summary>
/// All database options are optional.
/// </summary>
public record DatabaseOptions
{
    internal List<Assembly> _extraAssemblies = new ();

    /// <summary>
    /// The name of the connection string that will be used to read from appsettings.json or from ConnectionStringLoader.
    /// If not provided 'Default' will be used.
    /// </summary>
    public string DefaultConfigurationName { get; set; }

    /// <summary>
    /// This function can be provided to dynamically provide a connection string for a specific configuration.
    /// If it is not assigned or returns null, the configuration will be read from IConfiguration.
    /// </summary>
    public Func<ConfigurationName, IServiceProvider, Task<ConnectionString>> ConnectionStringLoader { get; set; }

    /// <summary>
    /// If true, all classes inheriting from IRepository will be registered. This value is default true.
    /// Use IServiceCollection to register repositories manually.
    /// </summary>
    public bool AutoRegisterRepositories { get; set; } = Constants.AutoRegisterRepositoriesDefault;

    /// <summary>
    /// If true, all classes inheriting from IRepositoryCollection will be registered. This value is default true.
    /// Use 'RegisterCollections' in 'DatabaseOptions' to register repositories manually.
    /// </summary>
    public bool AutoRegisterCollections { get; set; } = Constants.AutoRegisterCollectionsDefault;

    /// <summary>
    /// Provide manual registration of collections.
    /// </summary>
    public IEnumerable<CollectionType> RegisterCollections { get; set; }

    /// <summary>
    /// Override the list of assemblies scanned for automatic registration of IRepository and IRepositoryCollection.
    /// By default, only assemblies whose name starts with the same prefix as the entry-point assembly are scanned.
    /// Assemblies from external NuGet packages are NOT included by default — use <see cref="AddAutoRegistrationAssembly"/>
    /// to add them without replacing the default scan.
    /// </summary>
    public IEnumerable<Assembly> AutoRegistrationAssemblies { get; set; }

    /// <summary>
    /// Add additional assemblies for auto registration of IRepository and IRepositoryCollection.
    /// </summary>
    /// <param name="assembly"></param>
    public void AddAutoRegistrationAssembly(Assembly assembly)
    {
        _extraAssemblies.Add(assembly);
    }

    /// <summary>
    /// Event triggered on database actions performed on disk.
    /// </summary>
    public Action<ActionEventArgs> ActionEvent { get; set; }

    /// <summary>
    /// When provided this will override values in appsettings.json.
    /// Values in 'Configurations' will be used if they exist, otherwise the values in root will be used.
    /// Configuration order:
    /// 1. Named values from Configurations.
    /// 2. Values from the root in Configuration.
    /// 3. Named values from MongoDB-section in appsettings.json.
    /// 4. Values from the root in MongoDB-section in appsettings.json.
    /// 5. Default values.
    /// </summary>
    public Func<IServiceProvider, Task<MongoDbConfigurationTree>> ConfigurationLoader { get; set; }

    /// <summary>
    /// Controls how Guid values are stored in MongoDB.
    /// Standard (RFC 4122) is the default. Use CSharpLegacy only when working with existing legacy data.
    /// Individual properties can override this with [FlexibleGuid(GuidStorageFormat.X)].
    /// </summary>
    public GuidStorageFormat GuidStorageFormat { get; set; } = GuidStorageFormat.Standard;

    /// <summary>
    /// Enable or disable the assurance of incexes.
    /// By default, indexes are assured.
    /// </summary>
    public AssureIndexMode AssureIndex { get; set; } = AssureIndexMode.ByName;

    /// <summary>
    /// When <c>true</c>, every registered collection has its indexes assured during
    /// <c>UseMongoDB</c> at startup instead of waiting for the first access to each
    /// collection. Failures during the startup pass are logged and recorded in the
    /// in-process initiation state — they never throw, so the host always starts.
    /// Default <c>false</c> (lazy first-access assurance).
    /// </summary>
    public bool AssureIndexAtStartup { get; set; } = false;

    /// <summary>
    /// When non-null, registers a small background service that wakes on this cadence
    /// and re-attempts any index whose creation has previously failed. The service is
    /// idle in the steady state — ticks where no collection has a failed index return
    /// immediately, so a healthy app pays no cost. Set to <c>null</c> to disable
    /// (consumers with their own auditor or those who prefer strict "operator-explicit"
    /// recovery via <c>RestoreIndexAsync</c>). Default <c>TimeSpan.FromHours(1)</c>.
    /// </summary>
    public TimeSpan? FailedIndexRecheckInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Controls whether <see cref="Lockable.DocumentLease{T,TKey}.CommitAsync"/> may succeed for
    /// a lease whose lock has expired, provided no other writer has touched the document since
    /// (the <c>LockKey</c> atomicity check still drives the safety guarantee). Default <c>true</c>.
    /// Set to <c>false</c> to restore the strict-TTL behaviour where every expired commit throws
    /// <see cref="Lockable.LockExpiredException"/>. Individual collections can pin themselves to
    /// either policy by overriding the virtual <c>AllowDelayedCommit</c> property on
    /// <see cref="Lockable.LockableRepositoryCollectionBase{TEntity,TKey}"/>.
    /// </summary>
    public bool AllowDelayedCommit { get; set; } = true;

    /// <summary>
    /// Interval at which the Quilt4Net heartbeat service notifies Quilt4Net that the configured
    /// Atlas firewall openings are still in use. Each tick walks the active <c>MongoDbApiAccess</c>
    /// records and calls the appropriate proxy endpoint (<c>OpenAsync</c> for Open mode,
    /// <c>ReportUsedAsync</c> for Notify mode). Dormant when no access is in either mode, so the
    /// cost is zero when no consumer configures a <c>Quilt4NetApiKey</c>. Set to <c>null</c> to
    /// disable the heartbeat entirely. Default <c>TimeSpan.FromMinutes(5)</c>.
    /// </summary>
    public TimeSpan? Quilt4NetHeartbeatInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Configuration for monitor. This is by default enabled.
    /// </summary>
    public MonitorOptions Monitor { get; set; } = new();

    /// <summary>
    /// Configure database execution limiter.
    /// </summary>
    public ExecuteLimiterOptions Limiter { get; set; } = new();

    /// <summary>
    /// Optional callback that defers the monitor cache load until the system is ready.
    /// When set, the monitor starts immediately (API is usable) but the cache load
    /// is postponed until the provided callback action is invoked.
    /// The cache is loaded at most once, even if the callback is invoked multiple times.
    /// Example usage:
    /// <code>
    /// o.ReadyCallback = (serviceProvider, onReady) =>
    /// {
    ///     var config = serviceProvider.GetService&lt;IMyConfig&gt;();
    ///     config.ConfigurationUpdatedEvent += async (_, _) =>
    ///     {
    ///         if (config.HasConfiguration) await onReady();
    ///     };
    /// };
    /// </code>
    /// </summary>
    public Action<IServiceProvider, Func<Task>> ReadyCallback { get; set; }
}