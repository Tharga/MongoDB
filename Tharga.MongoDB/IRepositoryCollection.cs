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

    /// <summary>
    /// Drops the collection if it holds no documents. Collections are never dropped automatically, so this is the
    /// maintenance call for removing empty ones, such as per-part collections that are no longer used.
    /// MongoDB cannot drop a collection only if it is still empty, so the count and the drop are two steps, and a
    /// document added by another caller or process between them is lost with the collection. Call this only while
    /// nothing writes to the collection.
    /// A collection that declares a unique index is never dropped: other processes cannot see the drop and would
    /// recreate the collection without the index on their next write. Use <see cref="DropCollectionAsync"/> to drop
    /// it regardless.
    /// </summary>
    /// <returns>
    /// True if the collection was dropped. False if it does not exist, holds documents, or declares a unique index.
    /// </returns>
    Task<bool> DropIfEmptyAsync();
}

public interface IRepositoryCollection<TEntity, TKey> : IReadOnlyRepositoryCollection<TEntity, TKey>, IRepositoryCollection
    where TEntity : EntityBase<TKey>
{
    //Create
    Task AddAsync(TEntity entity, IClientSessionHandle session = null, CancellationToken cancellationToken = default);
    Task<bool> TryAddAsync(TEntity entity, IClientSessionHandle session = null, CancellationToken cancellationToken = default);
    Task AddManyAsync(IEnumerable<TEntity> entities, IClientSessionHandle session = null, CancellationToken cancellationToken = default);

    //Delete
    Task<TEntity> DeleteOneAsync(TKey id, IClientSessionHandle session = null, CancellationToken cancellationToken = default);

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