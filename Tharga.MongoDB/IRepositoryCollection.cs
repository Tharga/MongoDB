using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Tharga.MongoDB.Disk;

namespace Tharga.MongoDB;

public interface IRepositoryCollection : IReadOnlyRepositoryCollection
{
    Task DropCollectionAsync();
}

public interface IRepositoryCollection<TEntity, TKey> : IReadOnlyRepositoryCollection<TEntity, TKey>, IRepositoryCollection
    where TEntity : EntityBase<TKey>
{
    //Create
    Task AddAsync(TEntity entity);
    Task<bool> TryAddAsync(TEntity entity);
    Task AddManyAsync(IEnumerable<TEntity> entities);

    //Delete
    Task<TEntity> DeleteOneAsync(TKey id);

    Task<T> ExecuteAsync<T>(Func<IMongoCollection<TEntity>, Task<T>> execute, Operation operation);
    Task<T> ExecuteAsync<T>(Func<IMongoCollection<TEntity>, CancellationToken, Task<T>> execute, Operation operation, CancellationToken cancellationToken);
}