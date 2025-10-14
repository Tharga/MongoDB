using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace Tharga.MongoDB;

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