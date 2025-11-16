using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Extensions.Options;
using Tharga.MongoDB.Configuration;
using Tharga.MongoDB.Internals;

namespace Tharga.MongoDB;

internal class DatabaseMonitor : IDatabaseMonitor
{
    private readonly IMongoDbServiceFactory _mongoDbServiceFactory;
    private readonly IMongoDbInstance _mongoDbInstance;
    private readonly IServiceProvider _serviceProvider;
    private readonly IRepositoryConfiguration _repositoryConfiguration;
    private readonly DatabaseOptions _options;
    private readonly ConcurrentDictionary<DatabaseContext, CollectionAccessData> _accessedCollections = new();

    public DatabaseMonitor(IMongoDbServiceFactory mongoDbServiceFactory, IMongoDbInstance mongoDbInstance, IServiceProvider serviceProvider, IRepositoryConfiguration repositoryConfiguration, IOptions<DatabaseOptions> options)
    {
        _mongoDbServiceFactory = mongoDbServiceFactory;
        _mongoDbInstance = mongoDbInstance;
        _serviceProvider = serviceProvider;
        _repositoryConfiguration = repositoryConfiguration;
        _options = options.Value;

        _mongoDbServiceFactory.ConfigurationAccessEvent += (_, e) =>
        {

        };
        _mongoDbServiceFactory.CollectionAccessEvent += (_, e) =>
        {
            var now = DateTime.UtcNow;
            _accessedCollections.AddOrUpdate(e.DatabaseContext, new CollectionAccessData
            {
                DatabaseContext = e.DatabaseContext,
                EntityType = e.EntityType,
                CollectionType = e.CollectionType,
                FirstAccessed = now,
                LastAccessed = now,
                AccessCount = 1,
                //CollectionName = e.DatabaseContext.CollectionName,
                //ConfigurationName = e.DatabaseContext.ConfigurationName,
                Server = e.Server,
                //DatabasePart = e.DatabaseContext.DatabasePart
            }, (_, item) =>
            {
                item.LastAccessed = now;
                item.AccessCount++;
                return item;
            });
        };
    }

    public IEnumerable<ConfigurationName> GetConfigurations()
    {
        foreach (var item in _repositoryConfiguration.GetDatabaseConfigurationNames())
        {
            yield return item;
        }
    }

    //TODO: Should return registered indexes and actual indexes, so that we can find differences.
    //TODO: Should return information about database clean.
    //TODO: Have the option for a wide scan for all databases/collections on the server/configuration
    public async IAsyncEnumerable<CollectionInfo> GetInstancesAsync()
    {
        var configurations = GetConfigurations().Select(x => new DatabaseContext { ConfigurationName = x.Value });
        var contexts = configurations.Union(_accessedCollections.Select(x => new DatabaseContext
            {
                ConfigurationName = x.Value.DatabaseContext.ConfigurationName,
                DatabasePart = x.Value.DatabaseContext.DatabasePart
            }))
            .Distinct()
            .ToArray();

        var collectionsFromCode = GetStaticCollectionsFromCode().ToDictionary(x => (x.ConfigurationName ?? _options.DefaultConfigurationName, x.CollectionName), x => x);
        //var accessedCollections = _accessedCollections; //GetAccessed().ToDictionary(x => x.CollectionName, x => x);
        //var dynamicCollectionsFromCode = GetDynamicRegistrations().ToArray();

        foreach (var context in contexts)
        {
            await foreach (var inDatabase in GetCollectionsInDatabase(context))
            {
                var item = inDatabase;

                //Map Static registrations
                if (collectionsFromCode.TryGetValue((item.ConfigurationName, item.CollectionName), out var reg))
                {
                    //AssureSame(item.CollectionTypeName, reg.CollectionTypeName);

                    item = item with
                    {
                        Source = item.Source | reg.Source,
                        Registration = reg.Registration,
                        Types = item.Types.Union(reg.Types).ToArray(),
                        CollectionTypeName = item.CollectionTypeName ?? reg.CollectionTypeName
                    };
                    //TODO: Append information about registered indexes (so that we can compare with actual indexes)
                }
                else
                {
                    //NOTE: This collection exists but is not statically defined in code. (Perhaps it is dynamic or there is no code for it.)
                }

                    //Map Accessed collections
                    //Map Dynamic registrations

                    yield return item;
            }
        }

        //var staticRegistrations = collectionInfos.ToDictionary(x => (x.ConfigurationName ,x.CollectionName), x => x); //From Code
        //var dynamicRegistrations = GetDynamicRegistrations().ToDictionary(x => x.CollectionName, x => x); //From Code
        //var accessedCollections = GetAccessed().ToDictionary(x => x.CollectionName, x => x);

        //await foreach (var inDatabase in GetCollectionsInDatabase(databaseContext))
        //{
        //    var item = inDatabase;

        //    //Static registrations
        //    if (staticRegistrations.TryGetValue((inDatabase.ConfigurationName, inDatabase.CollectionName), out var reg))
        //    {
        //        AssureSame(item.CollectionTypeName, reg.CollectionTypeName);

        //        item = item with
        //        {
        //            Source = item.Source | reg.Source,
        //            Registration = reg.Registration,
        //            TypeNames = item.TypeNames.Union(reg.TypeNames).ToArray(),
        //            CollectionTypeName = item.CollectionTypeName ?? reg.CollectionTypeName
        //        };
        //        //TODO: Append information about registered indexes (so that we can compare with actual indexes)
        //    }
        //    else
        //    {
        //    }

        //    //Accessed collections
        //    if (accessedCollections.TryGetValue(inDatabase.CollectionName, out var t))
        //    {
        //        item = item with
        //        {
        //            Source = item.Source | t.Source,
        //            AccessCount = t.AccessCount,
        //            TypeNames = item.TypeNames.Union(t.TypeNames).ToArray(),
        //        };
        //    }
        //    else
        //    {
        //    }

        //    //Dynamic registrations (by type)
        //    if (dynamicRegistrations.TryGetValue(inDatabase.TypeNames.Single(), out var dyn))
        //    {
        //        AssureSame(item.CollectionTypeName, dyn.CollectionTypeName);

        //        item = item with
        //        {
        //            Source = item.Source | dyn.Source,
        //            Registration = Registration.Dynamic,
        //            CollectionTypeName = item.CollectionTypeName ?? dyn.CollectionTypeName
        //        };
        //        //TODO: Append information about registered indexes (so that we can compare with actual indexes)
        //    }
        //    else
        //    {
        //    }

        //    yield return item;
        //}
    }

    private IEnumerable<RCol> GetStaticCollectionsFromCode()
    {
        foreach (var registeredCollection in _mongoDbInstance.RegisteredCollections)
        {
            var isDynamic = registeredCollection.Value
                .GetConstructors()
                .Any(ctor => ctor.GetParameters()
                    .Any(param => param.ParameterType == typeof(DatabaseContext)));

            if (!isDynamic)
            {
                var genericParam = registeredCollection.Key
                    .GetInterfaces() // get all implemented interfaces
                    .Where(i => i.IsGenericType)
                    .Select(i => i.GetGenericArguments().FirstOrDefault())
                    .FirstOrDefault();
                //TODO: Get registered indexes (so we can compare with actual indexes)

                //NOTE: Create an instance and find the collection name.
                var instance = _serviceProvider.GetService(registeredCollection.Key) as RepositoryCollectionBase;
                if (instance == null) throw new InvalidOperationException($"Cannot create instance of '{registeredCollection.Key}'.");

                var item = new RCol
                {
                    Source = Source.Registration,
                    ConfigurationName = instance.ConfigurationName,
                    CollectionName = instance.CollectionName,
                    Types = [genericParam?.Name],
                    CollectionTypeName = registeredCollection.Key.Name,
                    Registration = Registration.Static
                };

                yield return item;
            }
        }
    }

    internal record RCol
    {
        public required Source Source { get; init; }
        public required string ConfigurationName { get; init; }
        public required string CollectionName { get; init; }
        public required string[] Types { get; init; }
        public required Registration Registration { get; init; }
        public required string CollectionTypeName { get; init; }
    }

    private IEnumerable<object> GetDynamicRegistrations()
    {
        foreach (var registeredCollection in _mongoDbInstance.RegisteredCollections)
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
                    //var item = new CollectionInfo
                    //{
                    //    Source = Source.Registration,
                    //    ConfigurationName = "?",
                    //    Server = "?",
                    //    DatabasePart = "?",
                    //    CollectionName = genericParam.Name,
                    //    TypeNames = [genericParam.Name],
                    //    CollectionTypeName = registeredCollection.Key.Name,
                    //    Registration = Registration.Dynamic
                    //};
                    //
                    //yield return item;
                    throw new NotImplementedException();
                }
                else
                {
                    Debugger.Break();
                }
            }
        }

        throw new NotImplementedException();
    }

    //private IEnumerable<CollectionInfo> GetAccessed()
    //{
    //    foreach (var accessedCollection in _accessedCollections)
    //    {
    //        yield return new CollectionInfo
    //        {
    //            Source = Source.Monitor,
    //            ConfigurationName = accessedCollection.Value.CollectionName,
    //            //Server = accessedCollection.Value.Server,
    //            //DatabasePart = accessedCollection.Value.DatabasePart,
    //            //CollectionName = accessedCollection.Key,
    //            //TypeNames = [accessedCollection.Value.EntityType.Name],
    //            //AccessCount = accessedCollection.Value.AccessCount
    //        };
    //    }
    //}

    private async IAsyncEnumerable<CollectionInfo> GetCollectionsInDatabase(DatabaseContext databaseContext)
    {
        var factory = _mongoDbServiceFactory.GetMongoDbService(() => databaseContext);
        var collections = await factory.GetCollectionsWithMetaAsync().ToArrayAsync();
        foreach (var collection in collections)
        {
            //if (collection.Name != collection.DatabaseContext.CollectionName) Debugger.Break();

            yield return new CollectionInfo
            {
                Source = Source.Database,
                ConfigurationName = collection.ConfigurationName, //DatabaseContext.ConfigurationName,
                //DatabasePart = collection.DatabaseContext.DatabasePart, //TODO: Is this relevant? Perhaps server and full database?
                //Server = collection.Server.Replace(collection.DatabaseName, ""),
                Server = collection.Server, //.Replace(collection.DatabaseName, ""),
                DatabaseName = collection.DatabaseName,
                //CollectionName = collection.DatabaseContext.CollectionName,
                //Database = collection.Types

                CollectionName = collection.CollectionName,

                DocumentCount = collection.DocumentCount,
                Size = collection.Size,
                Types = collection.Types,
                //Indexes = collection.Indexes,

                //Server = "?",
                //DatabasePart = collection.DatabaseContext.DatabasePart,
                //CollectionName = collection.Name,
                //TypeNames = collection.Types
            };
        }
    }

    private void AssureSame(string first, string second)
    {
        if (first == null) return;
        if (second == null) return;
        if (first != second) throw new InvalidOperationException($"Invalid difference between {first} and {second}.");
    }
}