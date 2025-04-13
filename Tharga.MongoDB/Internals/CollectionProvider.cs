using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using Tharga.MongoDB.Buffer;
using Tharga.MongoDB.Configuration;
using Tharga.MongoDB.Disk;

namespace Tharga.MongoDB.Internals;

internal class CollectionProvider : ICollectionProvider
{
    private readonly ICollectionProviderCache _collectionProviderCache;
    private readonly IMongoDbServiceFactory _mongoDbServiceFactory;
    private readonly Func<Type, object> _serviceLoader;
    private readonly Func<Type, Type> _typeLoader;

    public CollectionProvider(ICollectionProviderCache collectionProviderCache, IMongoDbServiceFactory mongoDbServiceFactory, Func<Type, object> serviceLoader, Func<Type, Type> typeLoader)
    {
        _collectionProviderCache = collectionProviderCache;
        _mongoDbServiceFactory = mongoDbServiceFactory;
        _serviceLoader = serviceLoader;
        _typeLoader = typeLoader;
    }

    public IRepositoryCollection<TEntity, TKey> GetGenericDiskCollection<TEntity, TKey>(DatabaseContext databaseContext) where TEntity : EntityBase<TKey>
    {
        if (typeof(TEntity).IsInterface) throw new NotSupportedException($"{nameof(GetGenericDiskCollection)} is not supported for interface '{typeof(TEntity).Name}'. Create a custom collection that implements '{nameof(IRepositoryCollection<TEntity, TKey>)}<{typeof(TEntity).Name},{typeof(TKey).Name}>' and call '{nameof(GetCollection)}' instead.");
        if (typeof(TEntity).IsAbstract) throw new NotSupportedException($"{nameof(GetGenericDiskCollection)} is not supported for abstract type '{typeof(TEntity).Name}'. Create a custom collection that implements '{nameof(IRepositoryCollection<TEntity, TKey>)}<{typeof(TEntity).Name},{typeof(TKey).Name}>' and call '{nameof(GetCollection)}' instead.");

        return _collectionProviderCache.GetCollection(databaseContext, dc =>
        {
            var logger = _serviceLoader(typeof(ILogger<RepositoryCollectionBase<TEntity, TKey>>)) as ILogger<RepositoryCollectionBase<TEntity, TKey>>;
            var collection = new GenericDiskRepositoryCollection<TEntity, TKey>(_mongoDbServiceFactory, dc, logger, null);
            return collection;
        });
    }

    public IRepositoryCollection<TEntity, TKey> GetGenericBufferCollection<TEntity, TKey>(DatabaseContext databaseContext) where TEntity : EntityBase<TKey>
    {
        if (typeof(TEntity).IsInterface) throw new NotSupportedException($"{nameof(GetGenericDiskCollection)} is not supported for interface '{typeof(TEntity).Name}'. Create a custom collection that implements '{nameof(IRepositoryCollection<TEntity, TKey>)}<{typeof(TEntity).Name},{typeof(TKey).Name}>' and call '{nameof(GetCollection)}' instead.");
        if (typeof(TEntity).IsAbstract) throw new NotSupportedException($"{nameof(GetGenericDiskCollection)} is not supported for abstract type '{typeof(TEntity).Name}'. Create a custom collection that implements '{nameof(IRepositoryCollection<TEntity, TKey>)}<{typeof(TEntity).Name},{typeof(TKey).Name}>' and call '{nameof(GetCollection)}' instead.");

        return _collectionProviderCache.GetCollection(databaseContext, dc =>
        {
            var logger = _serviceLoader(typeof(ILogger<BufferRepositoryCollectionBase<TEntity, TKey>>)) as ILogger<BufferRepositoryCollectionBase<TEntity, TKey>>;
            var collection = new GenericBufferRepositoryCollection<TEntity, TKey>(_mongoDbServiceFactory, logger, dc);
            return collection;
        });
    }

    public TCollection GetCollection<TCollection, TEntity, TKey>(DatabaseContext databaseContext)
        where TCollection : IReadOnlyRepositoryCollection<TEntity, TKey>
        where TEntity : EntityBase<TKey>
    {
        return _collectionProviderCache.GetCollection(databaseContext, dc =>
        {

            var collectionType = typeof(TCollection);
            if (collectionType.IsInterface)
            {
                var implementationType = _typeLoader(collectionType);
                collectionType = implementationType ?? throw new InvalidOperationException($"Cannot find a registered implementation for collection '{collectionType.Name}'. Turn on {nameof(DatabaseOptions.AutoRegisterCollections)} or provide collection manually with {nameof(DatabaseOptions.RegisterCollections)}.");
            }

            var ctor = collectionType.GetConstructors();
            if (ctor.Length != 1) throw new NotSupportedException($"There needs to be one single constructor for '{collectionType.Name}'.");

            var databaseContextProvided = false;
            var parameters = ctor.Single().GetParameters().Select(parameterInfo =>
            {
                switch (parameterInfo.ParameterType.Name)
                {
                    case nameof(DatabaseContext):
                        databaseContextProvided = true;
                        return dc;
                    default:
                        var item = _serviceLoader(parameterInfo.ParameterType);
                        return item;
                }
            }).ToArray();

            if (!databaseContextProvided && dc != null) throw new InvalidOperationException($"DatabaseContext was provided but the constructor for '{collectionType.Name}' does not recieve it. Add DatabaseContext to the constructor of '{collectionType.Name}' to get this to work.");

            var collection = Activator.CreateInstance(collectionType, parameters);
            return (TCollection)collection;
        });
    }

    public TCollection GetCollection<TCollection, TEntity>(DatabaseContext databaseContext = null) where TCollection : IRepositoryCollection<TEntity, ObjectId> where TEntity : EntityBase
    {
        return GetCollection<TCollection, TEntity, ObjectId>(databaseContext);
    }
}