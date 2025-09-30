using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using Tharga.MongoDB.Internals;

namespace Tharga.MongoDB.Configuration;

/// <summary>
/// All database options are optional.
/// </summary>
public record DatabaseOptions
{
    internal DatabaseOptions()
    {
    }

    /// <summary>
    /// The name of the connection string that will be used to read from appsettings.json or from ConnectionStringLoader.
    /// If not provided 'Default' will be used.
    /// </summary>
    public ConfigurationName ConfigurationName { get; set; }

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
    /// Assemblies stat starts with the same name are registered automatically. (IE. namespace "[same name].[other parts]")
    /// Provide a list of assemblies where automatic registration should be done instead of that.
    /// </summary>
    public IEnumerable<Assembly> AutoRegistrationAssemblies { get; set; }

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
    /// Collections provided by CollectionProvider are Cache.
    /// The cache is keepts for the duration of the application lifetime.
    /// This cache enabled by default.
    /// </summary>
    public bool UseCollectionProviderCache { get; set; } = true;

    /// <summary>
    /// Log level for execution information.
    /// </summary>
    public LogLevel? ExecuteInfoLogLevel { get; set; } = LogLevel.Debug;

    /// <summary>
    /// Set the Guid Representation in the database.
    /// CSharpLegacy is default.
    /// </summary>
    public GuidRepresentation? GuidRepresentation { get; set; }


    /// <summary>
    /// Enable or disable the assurance of incexes.
    /// By default, indexes are assured.
    /// </summary>
    public bool AssureIndex { get; set; } = true;
}