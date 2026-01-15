using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Driver;

namespace Tharga.MongoDB;

public interface IReadOnlyRepositoryCollection
{
    Task<long> GetSizeAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// This is a common interface for basic function that would be implemented in lockable collection.
/// </summary>
/// <typeparam name="TEntity"></typeparam>
/// <typeparam name="TKey"></typeparam>
public interface IReadOnlyRepositoryCollection<TEntity, TKey> : IReadOnlyRepositoryCollection
    where TEntity : EntityBase<TKey>
{
    //Read
    IAsyncEnumerable<TEntity> GetAsync(Expression<Func<TEntity, bool>> predicate = null, Options<TEntity> options = null, CancellationToken cancellationToken = default);
    IAsyncEnumerable<TEntity> GetAsync(FilterDefinition<TEntity> filter, Options<TEntity> options = null, CancellationToken cancellationToken = default);

    IAsyncEnumerable<T> GetProjectionAsync<T>(Expression<Func<T, bool>> predicate = null, Options<T> options = null, CancellationToken cancellationToken = default);
    IAsyncEnumerable<T> GetProjectionAsync<T>(FilterDefinition<T> filter, Options<T> options = null, CancellationToken cancellationToken = default);

    Task<Result<TEntity, TKey>> GetManyAsync(Expression<Func<TEntity, bool>> predicate = null, Options<TEntity> options = null, CancellationToken cancellationToken = default);
    Task<Result<TEntity, TKey>> GetManyAsync(FilterDefinition<TEntity> filter, Options<TEntity> options = null, CancellationToken cancellationToken = default);

    Task<Result<T>> GetManyProjectionAsync<T>(Expression<Func<T, bool>> predicate = null, Options<T> options = null, CancellationToken cancellationToken = default);
    Task<Result<T>> GetManyProjectionAsync<T>(FilterDefinition<T> filter, Options<T> options = null, CancellationToken cancellationToken = default);

    Task<TEntity> GetOneAsync(TKey id, CancellationToken cancellationToken = default);
    Task<TEntity> GetOneAsync(Expression<Func<TEntity, bool>> predicate = null, OneOption<TEntity> options = null, CancellationToken cancellationToken = default);
    Task<TEntity> GetOneAsync(FilterDefinition<TEntity> filter, OneOption<TEntity> options = null, CancellationToken cancellationToken = default);

    Task<long> CountAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);
    Task<long> CountAsync(FilterDefinition<TEntity> filter, CancellationToken cancellationToken = default);
}

public interface IDiskReadOnlyRepositoryCollection<TEntity, TKey> : IReadOnlyRepositoryCollection<TEntity, TKey>
    where TEntity : EntityBase<TKey>
{
    //[Obsolete("Projection methods will have to be developed if needed.")]
    //IAsyncEnumerable<T> GetAsync<T>(Expression<Func<T, bool>> predicate = null, Options<T> options = null, CancellationToken cancellationToken = default) where T : TEntity;

    [Obsolete($"Use {nameof(GetManyAsync)} instead. This method will be deprecated.")]
    Task<Result<TEntity, TKey>> QueryAsync(Expression<Func<TEntity, bool>> predicate = null, Options<TEntity> options = null, CancellationToken cancellationToken = default);

    [Obsolete($"Use {nameof(GetManyAsync)} instead. This method will be deprecated.")]
    Task<Result<TEntity, TKey>> QueryAsync(FilterDefinition<TEntity> filter, Options<TEntity> options = null, CancellationToken cancellationToken = default);

    //[Obsolete($"Use {nameof(GetManyAsync)} instead. This method will be deprecated.")]
    //IAsyncEnumerable<ResultPage<TEntity, TKey>> GetPagesAsync(Expression<Func<TEntity, bool>> predicate, Options<TEntity> options = null, CancellationToken cancellationToken = default);

    //[Obsolete($"Use {nameof(GetManyAsync)} instead. This method will be deprecated.")]
    //IAsyncEnumerable<ResultPage<TEntity, TKey>> GetPagesAsync(FilterDefinition<TEntity> filter, Options<TEntity> options = null, CancellationToken cancellationToken = default);

    //[Obsolete($"Use {nameof(GetManyAsync)} instead. This method will be deprecated.")]
    //Task<T> GetOneAsync<T>(Expression<Func<T, bool>> predicate = null, OneOption<T> options = null, CancellationToken cancellationToken = default) where T : TEntity;

    //[Obsolete($"Use {nameof(GetManyAsync)} instead. This method will be deprecated.")]
    //Task<T> GetOneProjectionAsync<T>(Expression<Func<T, bool>> predicate = null, OneOption<T> options = null, CancellationToken cancellationToken = default);

    //Other

    /// <summary>
    /// Entities that needs cleaning.
    /// </summary>
    /// <returns></returns>
    IAsyncEnumerable<TEntity> GetDirtyAsync();

    /// <summary>
    /// Indices that have failed to be created.
    /// </summary>
    /// <returns></returns>
    IEnumerable<(IndexFailOperation Operation, string Name)> GetFailedIndices();
}