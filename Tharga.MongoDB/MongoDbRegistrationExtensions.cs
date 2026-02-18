using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Tharga.MongoDB.Atlas;
using Tharga.MongoDB.Configuration;
using Tharga.MongoDB.Internals;
using Tharga.Runtime;

namespace Tharga.MongoDB;

public static class MongoDbRegistrationExtensions
{
    private static Action<ActionEventArgs> _actionEvent;

    public static IServiceCollection AddMongoDB(this IHostApplicationBuilder builder, Action<DatabaseOptions> options = null)
    {
        return AddMongoDB(builder.Services, builder.Configuration, options);
    }

    public static IServiceCollection AddMongoDB(this IServiceCollection services, IConfiguration configuration, Action<DatabaseOptions> options = null)
    {
        var mongoDbInstance = new MongoDbInstance();

        var c = configuration.GetSection("MongoDB").Get<DatabaseOptions>();

        var mo = new MonitorOptions();
        var lo = new ExecuteLimiterOptions();

        var o = new DatabaseOptions
        {
            DefaultConfigurationName = c?.DefaultConfigurationName ?? Constants.DefaultConfigurationName,
            AutoRegisterRepositories = c?.AutoRegisterRepositories ?? Constants.AutoRegisterRepositoriesDefault,
            AutoRegisterCollections = c?.AutoRegisterCollections ?? Constants.AutoRegisterCollectionsDefault,
            ExecuteInfoLogLevel = c?.ExecuteInfoLogLevel ?? LogLevel.Debug,
            GuidStorageFormat = c?.GuidStorageFormat ?? new DatabaseOptions().GuidStorageFormat,
            AssureIndex = c?.AssureIndex ?? AssureIndexMode.ByName,
            Monitor = new MonitorOptions
            {
                Enabled = c?.Monitor?.Enabled ?? mo.Enabled,
                LastCallsToKeep = c?.Monitor?.LastCallsToKeep ?? mo.LastCallsToKeep,
                SlowCallsToKeep = c?.Monitor?.SlowCallsToKeep ?? mo.SlowCallsToKeep,
            },
            Limiter = new ExecuteLimiterOptions
            {
                Enabled = c?.Limiter?.Enabled ?? lo.Enabled,
                MaxConcurrent = c?.Limiter?.MaxConcurrent ?? lo.MaxConcurrent,
            }
        };
        options?.Invoke(o);
        services.AddSingleton(Options.Create(o));
        services.AddSingleton(Options.Create(o.Limiter));

        BsonSerializer.TryRegisterSerializer(new FlexibleGuidSerializer(o.GuidStorageFormat));

        _actionEvent = o.ActionEvent;
        _actionEvent?.Invoke(new ActionEventArgs(new ActionEventArgs.ActionData { Message = $"Entering {nameof(AddMongoDB)}.", Level = LogLevel.Debug }, new ActionEventArgs.ContextData()));

        RepositoryCollectionBase.ActionEvent += (_, e) => { _actionEvent?.Invoke(e); };

        services.AddAssemblyService();

        services.AddHttpClient();

        services.AddTransient<IExternalIpAddressService, ExternalIpAddressService>();
        services.AddTransient<IMongoDbFirewallService, MongoDbFirewallService>();
        services.AddSingleton<IMongoDbClientProvider, MongoDbClientProvider>();
        services.AddSingleton<IMongoDbFirewallStateService, MongoDbFirewallStateService>();
        services.AddSingleton<IExecuteLimiter, ExecuteLimiter>();
        services.AddSingleton<ICollectionPool, CollectionPool>();
        services.AddSingleton<IInitiationLibrary, InitiationLibrary>();
        services.AddSingleton<ICollectionProviderCache, CollectionProviderCache>();
        services.AddSingleton<IMongoDbServiceFactory>(serviceProvider =>
        {
            var mongoDbClientProvider = serviceProvider.GetService<IMongoDbClientProvider>();
            var repositoryConfigurationLoader = serviceProvider.GetService<IRepositoryConfigurationLoader>();
            var mongoDbFirewallStateService = serviceProvider.GetService<IMongoDbFirewallStateService>();
            var executeLimiter = serviceProvider.GetService<IExecuteLimiter>();
            var collectionPool = serviceProvider.GetService<ICollectionPool>();
            var initiationLibrary = serviceProvider.GetService<IInitiationLibrary>();
            var logger = serviceProvider.GetService<ILogger<MongoDbServiceFactory>>();
            return new MongoDbServiceFactory(mongoDbClientProvider, repositoryConfigurationLoader, mongoDbFirewallStateService, executeLimiter, collectionPool, initiationLibrary, logger);
        });
        services.AddTransient<IRepositoryConfigurationLoader>(serviceProvider =>
        {
            var mongoUrlBuilderLoader = serviceProvider.GetService<IMongoUrlBuilderLoader>();
            var repositoryConfiguration = serviceProvider.GetService<IRepositoryConfiguration>();
            return new RepositoryConfigurationLoader(mongoUrlBuilderLoader, repositoryConfiguration, o);
        });
        services.AddTransient<IMongoUrlBuilderLoader>(serviceProvider => new MongoUrlBuilderLoader(serviceProvider, o));
        services.AddTransient<IRepositoryConfiguration>(serviceProvider => new RepositoryConfiguration(serviceProvider, o));
        services.AddTransient<ICollectionProvider, CollectionProvider>(provider =>
        {
            var collectionPool = provider.GetService<ICollectionPool>();
            var collectionProviderCache = provider.GetService<ICollectionProviderCache>();
            var mongoDbServiceFactory = provider.GetService<IMongoDbServiceFactory>();
            return new CollectionProvider(collectionPool, collectionProviderCache, mongoDbServiceFactory, type =>
            {
                var service = provider.GetService(type);
                return service;
            }, type =>
            {
                mongoDbInstance.RegisteredCollections.TryGetValue(type, out var implementationType);
                return implementationType;
            });
        });

        if (o.AutoRegisterRepositories || o.AutoRegisterCollections)
        {
            _actionEvent?.Invoke(new ActionEventArgs(new ActionEventArgs.ActionData
            {
                Message = $"Looking for assemblies in {string.Join(", ", GetAssemblies(o).Select(x => x.GetName().Name).ToArray())}.",
                Level = LogLevel.Debug
            }, new ActionEventArgs.ContextData()));
        }

        if (o.AutoRegisterRepositories)
        {
            var currentDomainDefinedTypes = AssemblyService.GetTypes<IRepository>(x => !x.IsGenericType && !x.IsInterface, GetAssemblies(o)).ToArray();
            foreach (var repositoryType in currentDomainDefinedTypes)
            {
                var serviceTypes = repositoryType.ImplementedInterfaces
                    .Where(x => x.IsInterface && !x.IsGenericType && x != typeof(IRepository))
                    .Where(x => x.IsOfType<IRepository>())
                    .ToArray();
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

        if (o.RegisterCollections?.Any() ?? false)
        {
            foreach (var registerCollection in o.RegisterCollections)
            {
                RegisterCollection(services, mongoDbInstance, registerCollection.Interface, registerCollection.Implementation, "Manual");
            }
        }

        if (o.AutoRegisterCollections)
        {
            var currentDomainDefinedTypes = AssemblyService.GetTypes<IReadOnlyRepositoryCollection>(x => !x.IsGenericType && !x.IsInterface, GetAssemblies(o)).ToArray();
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
        services.AddSingleton<IMongoDbInstance>(mongoDbInstance);

        if (o.Monitor?.Enabled ?? false)
        {
            services.AddSingleton<IDatabaseMonitor, DatabaseMonitor>();
            services.AddSingleton<ICallLibrary, CallLibrary>();
        }
        else
        {
            services.AddSingleton<IDatabaseMonitor, DatabaseNullMonitor>();
        }

        return services;
    }

    private static IEnumerable<Assembly> GetAssemblies(DatabaseOptions o)
    {
        TryLoadCacheAssembly(o);
        return (o.AutoRegistrationAssemblies ?? AssemblyService.GetAssemblies()).Union(o._extraAssemblies);
    }

    private static void TryLoadCacheAssembly(DatabaseOptions o)
    {
        try
        {
            var cacheAssembly = Assembly.Load(new AssemblyName("Tharga.Cache.MongoDB"));
            o._extraAssemblies.Add(cacheAssembly);
        }
        catch
        {
            // ignored
        }
    }

    public static void UseMongoDB(this IHost app, Action<UseMongoOptions> options = null)
    {
        _actionEvent?.Invoke(new ActionEventArgs(new ActionEventArgs.ActionData { Message = $"Entering {nameof(UseMongoDB)}.", Level = LogLevel.Debug }, new ActionEventArgs.ContextData()));

        FlexibleGuidSerializer.Logger = app.Services.GetService<ILoggerFactory>()?.CreateLogger<FlexibleGuidSerializer>();

        var databaseOptions = app.Services.GetService<IOptions<DatabaseOptions>>();

        var mongoDbInstance = app.Services.GetService<IMongoDbInstance>();
        if (mongoDbInstance == null) throw new InvalidOperationException($"Tharga MongoDB has not been registered. Call {nameof(AddMongoDB)} first.");

        //NOTE: Set up default configuration
        var repositoryConfiguration = app.Services.GetService<IRepositoryConfiguration>();
        var o = new UseMongoOptions
        {
            DatabaseUsage = new DatabaseUsage
            {
                FirewallConfigurationNames = repositoryConfiguration.GetDatabaseConfigurationNames().ToArray()
            },
            OpenFirewall = true,
        };
        options?.Invoke(o);

        if (databaseOptions.Value.Monitor?.Enabled ?? false)
        {
            var monitor = app.Services.GetService<IDatabaseMonitor>() as DatabaseMonitor;
            monitor?.Start();
        }

        if (o.OpenFirewall)
        {
            _actionEvent?.Invoke(new ActionEventArgs(new ActionEventArgs.ActionData { Message = $"Found {o.DatabaseUsage.FirewallConfigurationNames.Length} database configurations. ({string.Join(", ", o.DatabaseUsage.FirewallConfigurationNames)})", Level = LogLevel.Debug }, new ActionEventArgs.ContextData()));

            var mongoDbFirewallStateService = app.Services.GetService<IMongoDbFirewallStateService>();
            var mongoDbServiceFactory = app.Services.GetService<IMongoDbServiceFactory>();

            var task = Task.Run(async () =>
            {
                try
                {
                    foreach (var configurationName in o.DatabaseUsage.FirewallConfigurationNames)
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
                                o.Logger?.LogInformation(message);
                            }
                            else
                            {
                                _actionEvent?.Invoke(new ActionEventArgs(new ActionEventArgs.ActionData { Message = $"Ignore firewall for {databaseHostName}.", Level = LogLevel.Information }, new ActionEventArgs.ContextData()));
                                o.Logger?.LogInformation("Ignore firewall for {hostname}.", databaseHostName);
                            }
                        }
                        else
                        {
                            _actionEvent?.Invoke(new ActionEventArgs(new ActionEventArgs.ActionData { Message = $"No firewall information for database configuration '{configurationName}'.", Level = LogLevel.Information }, new ActionEventArgs.ContextData()));
                            o.Logger?.LogInformation("No firewall information for database configuration '{configurationName}'.", configurationName);
                        }
                    }
                }
                catch (Exception e)
                {
                    _actionEvent?.Invoke(new ActionEventArgs(new ActionEventArgs.ActionData { Message = e.Message, Level = LogLevel.Critical, Exception = e }, new ActionEventArgs.ContextData()));
                    o.Logger?.LogError(e, e.Message);
                }

                _actionEvent?.Invoke(new ActionEventArgs(new ActionEventArgs.ActionData { Message = "Firewall open process complete.", Level = LogLevel.Debug }, new ActionEventArgs.ContextData()));
                o.Logger?.LogDebug("Firewall open process complete.");
            });

            if (o.WaitToComplete) Task.WaitAll(task);
        }

        if (o.AssureIndex)
        {
            var task = Task.Run(async () =>
            {
                var monitor = app.Services.GetService<IDatabaseMonitor>();
                await foreach (var collectionInfo in monitor.GetInstancesAsync().Where(x => x.Registration == Registration.Static))
                {
                    await monitor.RestoreIndexAsync(collectionInfo);
                }
            });

            if (o.WaitToComplete) Task.WaitAll(task);
        }
    }

    //internal static IServiceCollection RegisterMongoDBCollection<TRepositoryCollection, TRepositoryCollectionBase>(this IServiceCollection services)
    //    where TRepositoryCollection : IRepositoryCollection
    //    where TRepositoryCollectionBase : RepositoryCollectionBase
    //{
    //    //var provider = services.BuildServiceProvider(); //TODO: Not allowed to do this, singletons will be strange.
    //    //var mongoDbInstance = provider.GetService<IMongoDbInstance>();
    //    var implementationType = typeof(TRepositoryCollectionBase);
    //    var serviceType = typeof(TRepositoryCollection);

    //    RegisterCollection(services, mongoDbInstance, serviceType, implementationType, "Direct");
    //    return services;
    //}

    internal static void RegisterCollection(this IServiceCollection services, IMongoDbInstance mongoDbInstance, Type serviceType, Type implementationType, string regTypeName)
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