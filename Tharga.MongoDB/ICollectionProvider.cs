﻿using MongoDB.Bson;

namespace Tharga.MongoDB;

public interface ICollectionProvider
{
    /// <summary>
    /// This method will return a generic collection with the requested types. If you want to apply indexes, use GetCollection and provide a specific implementation instead.
    /// </summary>
    /// <typeparam name="TEntity"></typeparam>
    /// <typeparam name="TKey"></typeparam>
    /// <param name="databaseContext"></param>
    /// <returns></returns>
    IRepositoryCollection<TEntity, TKey> GetGenericDiskCollection<TEntity, TKey>(DatabaseContext databaseContext = null)
        where TEntity : EntityBase<TKey>;

    /// <summary>
    /// Returns a defined collection.
    /// </summary>
    /// <typeparam name="TCollection"></typeparam>
    /// <typeparam name="TEntity"></typeparam>
    /// <typeparam name="TKey"></typeparam>
    /// <param name="databaseContext"></param>
    /// <returns></returns>
    TCollection GetCollection<TCollection, TEntity, TKey>(DatabaseContext databaseContext = null)
        where TCollection : IReadOnlyRepositoryCollection<TEntity, TKey>
        where TEntity : EntityBase<TKey>;

    /// <summary>
    /// Returns a defined collection where the BaseEntity has ObjectId as key.
    /// </summary>
    /// <typeparam name="TCollection"></typeparam>
    /// <typeparam name="TEntity"></typeparam>
    /// <param name="databaseContext"></param>
    /// <returns></returns>
    TCollection GetCollection<TCollection, TEntity>(DatabaseContext databaseContext = null)
        where TCollection : IReadOnlyRepositoryCollection<TEntity, ObjectId>
        where TEntity : EntityBase;
}