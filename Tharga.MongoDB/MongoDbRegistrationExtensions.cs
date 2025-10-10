using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Tharga.MongoDB.Atlas;
using Tharga.MongoDB.Configuration;
using Tharga.MongoDB.Internals;
using Tharga.Toolkit.TypeService;

namespace Tharga.MongoDB;

public interface IDatabaseMonitor
{
    //Task RegisterInstanceAsync(string name);
    IAsyncEnumerable<string> GetInstancesAsync();
}

internal record CollectionAccessData
{
    public required Type EntityType { get; init; }
    public required Type CollectionType { get; init; }
    public DateTime FirstAccessed { get; internal set; }
    public DateTime LastAccessed { get; internal set; }
    public int AccessCount { get; internal set; }
}

internal class DatabaseMonitor : IDatabaseMonitor
{
    private readonly IMongoDbServiceFactory _mongoDbServiceFactory;
    private readonly IMongoDbInstance _mongoDbInstance;
    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentDictionary<string, CollectionAccessData> _accessedCollections = new();

    public DatabaseMonitor(IMongoDbServiceFactory mongoDbServiceFactory, IMongoDbInstance mongoDbInstance, IServiceProvider serviceProvider)
    {
        _mongoDbServiceFactory = mongoDbServiceFactory;
        _mongoDbInstance = mongoDbInstance;
        _serviceProvider = serviceProvider;

        _mongoDbServiceFactory.CollectionAccessEvent += (_, e) =>
        {
            var now = DateTime.UtcNow;
            _accessedCollections.AddOrUpdate(e.CollectionName, new CollectionAccessData { EntityType = e.EntityType, CollectionType = e.CollectionType, FirstAccessed = now, LastAccessed = now, AccessCount = 1 }, (_, item) =>
            {
                item.LastAccessed = now;
                item.AccessCount++;
                return item;
            });
        };
    }

    public async IAsyncEnumerable<string> GetInstancesAsync()
    {
        var collectionsInDatabase = new Dictionary<string, Dbi>(); //Collection
        var dynamicRegistration = new Dictionary<string, Dbi>(); //Dynamic registration
        var staticRegistrations = new Dictionary<string, Dbi>(); //Static registration
        var touched = new Dictionary<string, Dbi>(); //Touched

        //1. List all collections in the database
        var factory = _mongoDbServiceFactory.GetMongoDbService(() => new DatabaseContext());
        var collections = await factory.GetCollectionsWithMetaAsync().ToArrayAsync();
        foreach (var collection in collections)
        {
            //TODO: Append index information from the actual collections
            var value = new Dbi { CollectionName = collection.Name, Types = collection.Types };
            collectionsInDatabase.Add(value.CollectionName, value);
        }

        //2. List all collection types from assembly (static and dynamic)
        var registeredCollections = _mongoDbInstance.RegisteredCollections;
        foreach (var registeredCollection in registeredCollections)
        {
            var isDynamic = registeredCollection.Value
                .GetConstructors()
                .Any(ctor => ctor.GetParameters()
                    .Any(param => param.ParameterType == typeof(DatabaseContext)));

            if (isDynamic)
            {
                var genericParam = registeredCollection.Key
                    .GetInterfaces()
                    .Where(i => i.IsGenericType)
                    .Select(i => i.GetGenericArguments().FirstOrDefault())
                    .FirstOrDefault();

                if (genericParam?.Name != null)
                {
                    dynamicRegistration.Add(genericParam?.Name, new Dbi { Types = [genericParam?.Name], CollectionTypeName = registeredCollection.Key.Name });
                }
                else
                {
                    Debugger.Break();
                }
            }
            else
            {
                var genericParam = registeredCollection.Key
                    .GetInterfaces() // get all implemented interfaces
                    .Where(i => i.IsGenericType)
                    .Select(i => i.GetGenericArguments().FirstOrDefault())
                    .FirstOrDefault();
                //TODO: Get registered indexes (so we can compare with actual indexes)

                //NOTE: Create an instance and find the collection name.
                var tt = _serviceProvider.GetService(registeredCollection.Key) as RepositoryCollectionBase;
                if (tt == null) throw new InvalidOperationException($"Cannot create instance of '{registeredCollection.Key}'.");
                staticRegistrations.Add(tt?.CollectionName, new Dbi { CollectionName = tt?.CollectionName, Types = [genericParam?.Name], CollectionTypeName = registeredCollection.Key.Name });
            }
        }

        //3. Look at instances to see what have been touched, so we can match them.
        var accessedCollections = _accessedCollections.ToArray();
        foreach (var accessedCollection in accessedCollections)
        {
            //TODO: Should be able to contain more than one type
            touched.Add(accessedCollection.Key, new Dbi { CollectionName = accessedCollection.Key, Types = [accessedCollection.Value.EntityType.Name], AccessCount = accessedCollection.Value.AccessCount });
        }

        foreach (var item in collectionsInDatabase)
        {
            //Static registrations
            if (staticRegistrations.TryGetValue(item.Key, out var reg))
            {
                item.Value.Registration = Registration.Static;
                item.Value.Types = (reg.Types ?? []).Union(item.Value.Types ?? []).ToArray();
                item.Value.CollectionTypeName = reg.CollectionTypeName;
                //TODO: Append information about registered indexes (so that we can compare with actual indexes)
            }

            //Touched values
            if (touched.TryGetValue(item.Key, out var t))
            {
                item.Value.AccessCount = t.AccessCount;
                item.Value.Types = (t.Types ?? []).Union(item.Value.Types ?? []).ToArray();
            }

            //Find dynamic registrations by type
            if (dynamicRegistration.TryGetValue(item.Value.Types.Single(), out var dyn))
            {
                item.Value.Registration = Registration.Dynamic;
                item.Value.CollectionTypeName = dyn.CollectionTypeName;
                //TODO: Append information about registered indexes (so that we can compare with actual indexes)
            }

            //TODO: Verify index difference between registration and actual types.
            //TODO: Show information about when the collections was cleaned.

            yield return $"{item.Key}: {item.Value}";
        }
    }
}

public interface ICollectionTypeService
{
    IEnumerable<CollectionType> GetCollectionTypes();
}

internal class CollectionTypeService : ICollectionTypeService
{
    private readonly IServiceProvider _serviceProvider;

    public CollectionTypeService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IEnumerable<CollectionType> GetCollectionTypes()
    {
        var mongoDbInstance = _serviceProvider.GetService<IMongoDbInstance>();
        if (mongoDbInstance == null) throw new InvalidOperationException($"Tharga MongoDB has not been registered.");

        ConcurrentDictionary<Type, Type> cols = ((MongoDbInstance)mongoDbInstance).RegisteredCollections;
        return cols.Select(x =>
        {
            var isDynamic = x.Value
                .GetConstructors()
                .Any(ctor => ctor.GetParameters()
                    .Any(param => param.ParameterType == typeof(DatabaseContext)));

            return new CollectionType
            {
                ServiceType = x.Key,
                ImplementationType = x.Value,
                IsDynamic = isDynamic
            };
        });
    }
}

public record CollectionType
{
    public required Type ServiceType { get; init; }
    public required Type ImplementationType { get; init; }
    public required bool IsDynamic { get; init; }
}

public static class MongoDbRegistrationExtensions
{
    private static Action<ActionEventArgs> _actionEvent;

    public static IServiceCollection AddMongoDB(this IServiceCollection services, Action<DatabaseOptions> options = null)
    {
        var mongoDbInstance = new MongoDbInstance();

        var databaseOptions = new DatabaseOptions
        {
            ConfigurationName = "Default",
            AutoRegisterRepositories = Constants.AutoRegisterRepositoriesDefault,
            AutoRegisterCollections = Constants.AutoRegisterCollectionsDefault,
            ExecuteInfoLogLevel = LogLevel.Debug
        };

        options?.Invoke(databaseOptions);

        BsonSerializer.TryRegisterSerializer(new GuidSerializer(databaseOptions.GuidRepresentation ?? GuidRepresentation.CSharpLegacy));

        _actionEvent = databaseOptions.ActionEvent;
        _actionEvent?.Invoke(new ActionEventArgs(new ActionEventArgs.ActionData { Message = $"Entering {nameof(AddMongoDB)}.", Level = LogLevel.Debug }, new ActionEventArgs.ContextData()));

        RepositoryCollectionBase.ActionEvent += (_, e) => { _actionEvent?.Invoke(e); };

        services.AddAssemblyService();

        services.AddHttpClient();

        services.AddTransient<IExternalIpAddressService, ExternalIpAddressService>();
        services.AddTransient<IMongoDbFirewallService, MongoDbFirewallService>();
        services.AddSingleton<IMongoDbFirewallStateService, MongoDbFirewallStateService>();
        services.AddSingleton<IMongoDbServiceFactory>(serviceProvider =>
        {
            var repositoryConfigurationLoader = serviceProvider.GetService<IRepositoryConfigurationLoader>();
            var mongoDbFirewallStateService = serviceProvider.GetService<IMongoDbFirewallStateService>();
            //var databaseMonitor = serviceProvider.GetService<IDatabaseMonitor>();
            var logger = serviceProvider.GetService<ILogger<MongoDbServiceFactory>>();
            return new MongoDbServiceFactory(repositoryConfigurationLoader, mongoDbFirewallStateService, /*databaseMonitor,*/ logger);
        });
        services.AddTransient<IRepositoryConfigurationLoader>(serviceProvider =>
        {
            var mongoUrlBuilderLoader = serviceProvider.GetService<IMongoUrlBuilderLoader>();
            var repositoryConfiguration = serviceProvider.GetService<IRepositoryConfiguration>();
            return new RepositoryConfigurationLoader(mongoUrlBuilderLoader, repositoryConfiguration, databaseOptions);
        });
        services.AddTransient<IMongoUrlBuilderLoader>(serviceProvider => new MongoUrlBuilderLoader(serviceProvider, databaseOptions));
        services.AddTransient<IRepositoryConfiguration>(serviceProvider => new RepositoryConfiguration(serviceProvider, databaseOptions));

        services.AddSingleton<ICollectionProviderCache>(_ =>
        {
            if (databaseOptions.UseCollectionProviderCache)
                return new CollectionProviderCache();
            return new CollectionProviderNoCache();
        });

        services.AddTransient<ICollectionProvider, CollectionProvider>(provider =>
        {
            var collectionProviderCache = provider.GetService<ICollectionProviderCache>();
            var mongoDbServiceFactory = provider.GetService<IMongoDbServiceFactory>();
            return new CollectionProvider(collectionProviderCache, mongoDbServiceFactory, type =>
            {
                var service = provider.GetService(type);
                return service;
            }, type =>
            {
                mongoDbInstance.RegisteredCollections.TryGetValue(type, out var implementationType);
                return implementationType;
            });
        });

        if (databaseOptions.AutoRegisterRepositories || databaseOptions.AutoRegisterCollections)
        {
            _actionEvent?.Invoke(new ActionEventArgs(new ActionEventArgs.ActionData
            {
                Message = $"Looking for assemblies in {string.Join(", ", (databaseOptions.AutoRegistrationAssemblies ?? AssemblyService.GetAssemblies()).Select(x => x.GetName().Name).ToArray())}.",
                Level = LogLevel.Debug
            }, new ActionEventArgs.ContextData()));
        }

        if (databaseOptions.AutoRegisterRepositories)
        {
            var currentDomainDefinedTypes = AssemblyService.GetTypes<IRepository>(x => !x.IsGenericType && !x.IsInterface, databaseOptions.AutoRegistrationAssemblies).ToArray();
            foreach (var repositoryType in currentDomainDefinedTypes)
            {
                var serviceTypes = repositoryType.ImplementedInterfaces.Where(x => x.IsInterface && !x.IsGenericType && x != typeof(IRepository)).ToArray();
                if (serviceTypes.Length > 1) throw new InvalidOperationException($"There are {serviceTypes.Length} interfaces for repository type '{repositoryType.Name}' ({string.Join(", ", serviceTypes.Select(x => x.Name))}).");
                var implementationType = repositoryType.AsType();
                var serviceType = serviceTypes.Length == 0 ? implementationType : serviceTypes.Single();

                if (!mongoDbInstance.RegisteredRepositories.TryAdd(serviceType, implementationType))
                {
                    mongoDbInstance.RegisteredRepositories.TryGetValue(serviceType, out var other);
                    if (implementationType.AssemblyQualifiedName != other?.AssemblyQualifiedName)
                    {
                        throw new InvalidOperationException($"There are multiple implementations for interface '{serviceType.Name}' (\n'{implementationType.AssemblyQualifiedName}' and \n'{other?.AssemblyQualifiedName}'). {nameof(DatabaseOptions.AutoRegisterRepositories)} in {nameof(DatabaseOptions)} cannot be used.");
                    }

                    _actionEvent?.Invoke(new ActionEventArgs(new ActionEventArgs.ActionData
                    {
                        Message = $"Trying to register the same repository types twice. Perhaps two assemblies in the same application are set to automatically register repositories.",
                        Level = LogLevel.Warning
                    }, new ActionEventArgs.ContextData()));
                }
                else
                {
                    services.AddTransient(serviceType, implementationType);

                    _actionEvent?.Invoke(new ActionEventArgs(new ActionEventArgs.ActionData
                    {
                        Message = $"Auto registered repository {serviceType.Name} ({implementationType.Name}).",
                        Level = LogLevel.Debug
                    }, new ActionEventArgs.ContextData()));
                }
            }
        }

        if (databaseOptions.RegisterCollections?.Any() ?? false)
        {
            foreach (var registerCollection in databaseOptions.RegisterCollections)
            {
                RegisterCollection(services, mongoDbInstance, registerCollection.Interface, registerCollection.Implementation, "Manual");
            }
        }

        if (databaseOptions.AutoRegisterCollections)
        {
            var currentDomainDefinedTypes = AssemblyService.GetTypes<IReadOnlyRepositoryCollection>(x => !x.IsGenericType && !x.IsInterface, databaseOptions.AutoRegistrationAssemblies).ToArray();
            foreach (var collectionType in currentDomainDefinedTypes)
            {
                var serviceTypes = collectionType.ImplementedInterfaces
                    .Where(x => x.IsInterface && !x.IsGenericType)
                    .Where(x => x != typeof(IReadOnlyRepositoryCollection))
                    .Where(x => x != typeof(IRepositoryCollection))
                    .ToArray();
                if (serviceTypes.Length > 1) throw new InvalidOperationException($"There are {serviceTypes.Length} interfaces for collection type '{collectionType.Name}' ({string.Join(", ", serviceTypes.Select(x => x.Name))}).");
                var implementationType = collectionType.AsType();
                var serviceType = serviceTypes.Length == 0 ? implementationType : serviceTypes.Single();

                RegisterCollection(services, mongoDbInstance, serviceType, implementationType, "Auto");
            }
        }

        services.AddTransient<ICollectionTypeService, CollectionTypeService>();
        services.AddSingleton<IDatabaseMonitor, DatabaseMonitor>();
        services.AddSingleton<IMongoDbInstance>(mongoDbInstance);

        return services;
    }

    //public static void UseMongoDB(this IServiceProvider services, Action<UseMongoOptions> options = null)
    //public static void UseMongoDB(this IApplicationBuilder app, Action<UseMongoOptions> options = null)
    public static void UseMongoDB(this IHost app, Action<UseMongoOptions> options = null)
    {
        app.Services.GetService<IDatabaseMonitor>();

        _actionEvent?.Invoke(new ActionEventArgs(new ActionEventArgs.ActionData { Message = $"Entering {nameof(UseMongoDB)}.", Level = LogLevel.Debug }, new ActionEventArgs.ContextData()));

        var mongoDbInstance = app.Services.GetService<IMongoDbInstance>();
        if (mongoDbInstance == null) throw new InvalidOperationException($"Tharga MongoDB has not been registered. Call {nameof(AddMongoDB)} first.");

        var repositoryConfiguration = app.Services.GetService<IRepositoryConfiguration>();

        var useMongoOptions = new UseMongoOptions
        {
            DatabaseUsage = new DatabaseUsage
            {
                FirewallConfigurationNames = repositoryConfiguration.GetDatabaseConfigurationNames().ToArray()
            }
        };

        options?.Invoke(useMongoOptions);

        _actionEvent?.Invoke(new ActionEventArgs(new ActionEventArgs.ActionData { Message = $"Found {useMongoOptions.DatabaseUsage.FirewallConfigurationNames.Length} database configurations. ({string.Join(", ", useMongoOptions.DatabaseUsage.FirewallConfigurationNames)})", Level = LogLevel.Debug }, new ActionEventArgs.ContextData()));

        var mongoDbFirewallStateService = app.Services.GetService<IMongoDbFirewallStateService>();
        var mongoDbServiceFactory = app.Services.GetService<IMongoDbServiceFactory>();

        var task = Task.Run(async () =>
        {
            try
            {
                foreach (var configurationName in useMongoOptions.DatabaseUsage.FirewallConfigurationNames)
                {
                    var configuration = repositoryConfiguration.GetConfiguration(configurationName);
                    if (configuration.AccessInfo.HasMongoDbApiAccess())
                    {
                        var mongoDbService = mongoDbServiceFactory.GetMongoDbService(() => new DatabaseContext { ConfigurationName = configurationName });
                        var databaseHostName = mongoDbService.GetDatabaseHostName();
                        if (!databaseHostName.Contains("localhost", StringComparison.InvariantCultureIgnoreCase))
                        {
                            var message = await mongoDbFirewallStateService.AssureFirewallAccessAsync(configuration.AccessInfo);
                            _actionEvent?.Invoke(new ActionEventArgs(new ActionEventArgs.ActionData { Message = message, Level = LogLevel.Information }, new ActionEventArgs.ContextData()));
                            useMongoOptions.Logger?.LogInformation(message);
                        }
                        else
                        {
                            _actionEvent?.Invoke(new ActionEventArgs(new ActionEventArgs.ActionData { Message = $"Ignore firewall for {databaseHostName}.", Level = LogLevel.Information }, new ActionEventArgs.ContextData()));
                            useMongoOptions.Logger?.LogInformation("Ignore firewall for {hostname}.", databaseHostName);
                        }
                    }
                    else
                    {
                        _actionEvent?.Invoke(new ActionEventArgs(new ActionEventArgs.ActionData { Message = $"No firewall information for database configuration '{configurationName}'.", Level = LogLevel.Information }, new ActionEventArgs.ContextData()));
                        useMongoOptions.Logger?.LogInformation("No firewall information for database configuration '{configurationName}'.", configurationName);
                    }
                }
            }
            catch (Exception e)
            {
                _actionEvent?.Invoke(new ActionEventArgs(new ActionEventArgs.ActionData { Message = e.Message, Level = LogLevel.Critical, Exception = e }, new ActionEventArgs.ContextData()));
                useMongoOptions.Logger?.LogError(e, e.Message);
            }

            _actionEvent?.Invoke(new ActionEventArgs(new ActionEventArgs.ActionData { Message = "Firewall open process complete.", Level = LogLevel.Debug }, new ActionEventArgs.ContextData()));
            useMongoOptions.Logger?.LogDebug("Firewall open process complete.");
        });

        if (useMongoOptions.WaitToComplete) Task.WaitAll(task);
    }

    internal static void RegisterCollection(IServiceCollection services, MongoDbInstance mongoDbInstance, Type serviceType, Type implementationType, string regTypeName)
    {
        if (!mongoDbInstance.RegisteredCollections.TryAdd(serviceType, implementationType))
        {
            if (mongoDbInstance.RegisteredCollections.TryGetValue(serviceType, out var other))
            {
                if (other == implementationType)
                {
                    _actionEvent?.Invoke(new ActionEventArgs(new ActionEventArgs.ActionData
                    {
                        Message = $"Collection {serviceType.Name} has already been manually registered.",
                        Level = LogLevel.Trace
                    }, new ActionEventArgs.ContextData()));
                    return;
                }
            }

            throw new InvalidOperationException($"There are multiple implementations for interface '{serviceType.Name}' ('{implementationType.AssemblyQualifiedName}' and '{other?.AssemblyQualifiedName}'). {nameof(DatabaseOptions.AutoRegisterCollections)} in {nameof(DatabaseOptions)} cannot be used.");
        }

        string message = null;
        var constructorInfos = implementationType.GetConstructors();
        if (constructorInfos.Any(x => x.GetParameters().All(y => y.ParameterType != typeof(DatabaseContext))))
        {
            services.AddTransient(serviceType, implementationType);
        }
        else
        {
            message = " Requires ICollectionProvider. Cannot be injected directly to constructor since it is not registered in IOC (IServiceCollection).";
        }

        _actionEvent?.Invoke(new ActionEventArgs(new ActionEventArgs.ActionData
        {
            Message = $"{regTypeName} registered collection {serviceType.Name} ({implementationType.Name}).{message}",
            Level = LogLevel.Debug
        }, new ActionEventArgs.ContextData()));
    }
}

public record Dbi
{
    public string CollectionName { get; set; }
    public string[] Types { get; set; }
    public int AccessCount { get; set; }
    public Registration Registration { get; set; }
    public string CollectionTypeName { get; set; }
}

public enum Registration
{
    Missing,
    Static,
    Dynamic
}