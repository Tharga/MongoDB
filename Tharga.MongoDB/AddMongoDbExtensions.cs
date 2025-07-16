using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Bson.Serialization;
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
    private static bool _hasBeenAdded;

    public static IServiceCollection AddMongoDB(this IServiceCollection services, Action<DatabaseOptions> options = null)
    {
        if (_hasBeenAdded) throw new InvalidOperationException("Tharga MongoDB has already been added.");
        _hasBeenAdded = true;

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

    public static void UseMongoDB(this IServiceProvider services, Action<UseMongoOptions> options = null)
    {
        _actionEvent?.Invoke(new ActionEventArgs(new ActionEventArgs.ActionData { Message = $"Entering {nameof(UseMongoDB)}.", Level = LogLevel.Debug }, new ActionEventArgs.ContextData()));

        if (!_hasBeenAdded) throw new InvalidOperationException($"Tharga MongoDB has not been added. Call {nameof(AddMongoDB)} first.");

        var repositoryConfiguration = services.GetService<IRepositoryConfiguration>();

        var useMongoOptions = new UseMongoOptions
        {
            DatabaseUsage = new DatabaseUsage
            {
                FirewallConfigurationNames = repositoryConfiguration.GetDatabaseConfigurationNames().ToArray()
            }
        };

        options?.Invoke(useMongoOptions);

        _actionEvent?.Invoke(new ActionEventArgs(new ActionEventArgs.ActionData { Message = $"Found {useMongoOptions.DatabaseUsage.FirewallConfigurationNames.Length} database configurations. ({string.Join(", ", useMongoOptions.DatabaseUsage.FirewallConfigurationNames)})", Level = LogLevel.Debug }, new ActionEventArgs.ContextData()));

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