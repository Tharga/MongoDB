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
    Task AddAsync(TEntity entity, IClientSessionHandle session = null);
    Task<bool> TryAddAsync(TEntity entity, IClientSessionHandle session = null);
    Task AddManyAsync(IEnumerable<TEntity> entities, IClientSessionHandle session = null);

    //Delete
    Task<TEntity> DeleteOneAsync(TKey id, IClientSessionHandle session = null);

    Task<T> ExecuteAsync<T>(Func<IMongoCollection<TEntity>, Task<T>> execute, Operation operation);
    Task<T> ExecuteAsync<T>(Func<IMongoCollection<TEntity>, CancellationToken, Task<T>> execute, Operation operation, CancellationToken cancellationToken);
    IAsyncEnumerable<T> ExecuteManyAsync<T>(Func<IMongoCollection<TEntity>, CancellationToken, Task<IAsyncCursor<T>>> queryFactory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Keyset (seek-based) pagination. Cost is O(log N) per page regardless of how deep the page sits —
    /// no skip penalty on deep pages or "jump to last." Single-column sort only; total count is intentionally
    /// not part of the result and should be obtained via <c>CountAsync(predicate)</c> separately.
    /// </summary>
    Task<Paging.CursorPage<TEntity>> GetPageAsync(
        int pageSize,
        Paging.PagePosition position,
        System.Linq.Expressions.Expression<Func<TEntity, bool>> predicate = null,
        System.Linq.Expressions.Expression<Func<TEntity, object>> sortBy = null,
        bool ascending = true,
        CancellationToken cancellationToken = default);

    /// <summary>Projection variant of <see cref="GetPageAsync"/> — returns the projected shape <typeparamref name="T"/> instead of the entity.</summary>
    Task<Paging.CursorPage<T>> GetPageProjectionAsync<T>(
        int pageSize,
        Paging.PagePosition position,
        System.Linq.Expressions.Expression<Func<TEntity, T>> projection,
        System.Linq.Expressions.Expression<Func<TEntity, bool>> predicate = null,
        System.Linq.Expressions.Expression<Func<TEntity, object>> sortBy = null,
        bool ascending = true,
        CancellationToken cancellationToken = default);
}