using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tharga.MongoDB.Atlas;
using Tharga.MongoDB.Configuration;
using Tharga.MongoDB.Internals;
using Tharga.Toolkit.TypeService;

namespace Tharga.MongoDB;

public static class AddMongoDbExtensions
{
    private static readonly ConcurrentDictionary<Type, Type> _registeredRepositories = new();
    private static readonly ConcurrentDictionary<Type, Type> _registeredCollections = new();
    private static Action<ActionEventArgs> _actionEvent;

    public static void UseMongoDB(this IServiceProvider services, Action<UseMongoOptions> options = null)
    {
        var repositoryConfiguration = services.GetService<IRepositoryConfiguration>();

        var useMongoOptions = new UseMongoOptions
        {
            DatabaseUsage = new DatabaseUsage { FirewallConfigurationNames = repositoryConfiguration.GetDatabaseConfigurationNames().ToArray() }
        };

        options?.Invoke(useMongoOptions);

        var mongoDbFirewallStateService = services.GetService<IMongoDbFirewallStateService>();
        var mongoDbServiceFactory = services.GetService<IMongoDbServiceFactory>();

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
                        if (!mongoDbService.GetDatabaseHostName().Contains("localhost", StringComparison.InvariantCultureIgnoreCase))
                        {
                            var message = await mongoDbFirewallStateService.AssureFirewallAccessAsync(configuration.AccessInfo);
                            useMongoOptions.Logger?.LogInformation(message);
                        }
                        else
                        {
                            useMongoOptions.Logger?.LogInformation("Ignore firewall for {hostname}.", mongoDbService.GetDatabaseHostName());
                        }
                    }
                    else
                    {
                        useMongoOptions.Logger?.LogInformation($"No firewall information for database configuration '{configurationName}'.");
                    }
                }
            }
            catch (Exception e)
            {
                useMongoOptions.Logger?.LogError(e, e.Message);
            }
        });

        if (useMongoOptions.WaitToComplete) Task.WaitAll(task);
    }

    public static IServiceCollection AddMongoDB(this IServiceCollection services, Action<DatabaseOptions> options = null)
    {
        var databaseOptions = new DatabaseOptions
        {
            ConfigurationName = "Default",
            AutoRegisterRepositories = Constants.AutoRegisterRepositoriesDefault,
            AutoRegisterCollections = Constants.AutoRegisterCollectionsDefault,
        };

        options?.Invoke(databaseOptions);

        _actionEvent = databaseOptions.ActionEvent;

        RepositoryCollectionBase.ActionEvent += (_, e) => { _actionEvent?.Invoke(e); };
        //AtlasAdministrationService.ActionEvent += (_, e) => { _actionEvent?.Invoke(e); };
        //MongoDbFirewallService.ActionEvent += (_, e) => { _actionEvent?.Invoke(e); };
        //ExternalIpAddressService.ActionEvent += (_, e) => { _actionEvent?.Invoke(e); };

        services.AddAssemblyService();

        services.AddHttpClient();

        services.AddTransient<IExternalIpAddressService, ExternalIpAddressService>();
        services.AddTransient<IMongoDbFirewallService, MongoDbFirewallService>();
        services.AddSingleton<IMongoDbFirewallStateService, MongoDbFirewallStateService>();
        services.AddSingleton<IMongoDbServiceFactory>(serviceProvider =>
        {
            var repositoryConfigurationLoader = serviceProvider.GetService<IRepositoryConfigurationLoader>();
            var mongoDbFirewallStateService = serviceProvider.GetService<IMongoDbFirewallStateService> ();
            var logger = serviceProvider.GetService<ILogger<MongoDbServiceFactory>>();
            return new MongoDbServiceFactory(repositoryConfigurationLoader, mongoDbFirewallStateService, logger);
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
                _registeredCollections.TryGetValue(type, out var implementationType);
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

                if (!_registeredRepositories.TryAdd(serviceType, implementationType))
                {
                    _registeredRepositories.TryGetValue(serviceType, out var other);
                    throw new InvalidOperationException($"There are multiple implementations for interface '{serviceType.Name}' ({implementationType.Name} and {other?.Name}). {nameof(DatabaseOptions.AutoRegisterRepositories)} in {nameof(DatabaseOptions)} cannot be used.");
                }
                services.AddTransient(serviceType, implementationType);

                _actionEvent?.Invoke(new ActionEventArgs(new ActionEventArgs.ActionData
                {
                    Message = $"Auto registered repository {serviceType.Name} ({implementationType.Name}).",
                    Level = LogLevel.Debug
                }, new ActionEventArgs.ContextData()));
            }
        }

        if (databaseOptions.RegisterCollections?.Any() ?? false)
        {
            foreach (var registerCollection in databaseOptions.RegisterCollections)
            {
                RegisterCollection(services, registerCollection.Interface, registerCollection.Implementation, "Manual");
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

                RegisterCollection(services, serviceType, implementationType, "Auto");
            }
        }

        return services;
    }

    private static void RegisterCollection(IServiceCollection services, Type serviceType, Type implementationType, string regTypeName)
    {
        if (!_registeredCollections.TryAdd(serviceType, implementationType))
        {
            if (_registeredCollections.TryGetValue(serviceType, out var other))
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

            throw new InvalidOperationException($"There are multiple implementations for interface '{serviceType.Name}' ({implementationType.Name} and {other?.Name}). {nameof(DatabaseOptions.AutoRegisterCollections)} in {nameof(DatabaseOptions)} cannot be used.");
        }

        string message = null;
        if (implementationType.GetConstructors().Any(x => x.GetParameters().All(y => y.ParameterType != typeof(DatabaseContext))))
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