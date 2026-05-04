using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Driver;

namespace Tharga.MongoDB.Internals;

/// <summary>
/// Session-aware wrappers around <see cref="IMongoCollection{T}"/> methods. Each method takes an
/// <see cref="IClientSessionHandle"/> that may be null — when non-null the driver's session overload is used,
/// otherwise the no-session overload. Helpers are suffixed (<c>...Maybe</c>) so they don't shadow the
/// driver's own overloads at call sites.
/// </summary>
internal static class MongoCollectionSessionExtensions
{
    public static Task InsertOneMaybeAsync<T>(
        this IMongoCollection<T> coll, IClientSessionHandle session, T document, CancellationToken cancellationToken = default)
        => session != null
            ? coll.InsertOneAsync(session, document, cancellationToken: cancellationToken)
            : coll.InsertOneAsync(document, cancellationToken: cancellationToken);

    public static Task InsertManyMaybeAsync<T>(
        this IMongoCollection<T> coll, IClientSessionHandle session, IEnumerable<T> documents, CancellationToken cancellationToken = default)
        => session != null
            ? coll.InsertManyAsync(session, documents, cancellationToken: cancellationToken)
            : coll.InsertManyAsync(documents, cancellationToken: cancellationToken);

    public static Task<T> FindOneAndReplaceMaybeAsync<T>(
        this IMongoCollection<T> coll, IClientSessionHandle session, FilterDefinition<T> filter, T replacement,
        FindOneAndReplaceOptions<T, T> options = null, CancellationToken cancellationToken = default)
        => session != null
            ? coll.FindOneAndReplaceAsync(session, filter, replacement, options, cancellationToken)
            : coll.FindOneAndReplaceAsync(filter, replacement, options, cancellationToken);

    public static Task<T> FindOneAndUpdateMaybeAsync<T>(
        this IMongoCollection<T> coll, IClientSessionHandle session, FilterDefinition<T> filter, UpdateDefinition<T> update,
        FindOneAndUpdateOptions<T, T> options = null, CancellationToken cancellationToken = default)
        => session != null
            ? coll.FindOneAndUpdateAsync(session, filter, update, options, cancellationToken)
            : coll.FindOneAndUpdateAsync(filter, update, options, cancellationToken);

    public static Task<UpdateResult> UpdateManyMaybeAsync<T>(
        this IMongoCollection<T> coll, IClientSessionHandle session, FilterDefinition<T> filter, UpdateDefinition<T> update,
        CancellationToken cancellationToken = default)
        => session != null
            ? coll.UpdateManyAsync(session, filter, update, cancellationToken: cancellationToken)
            : coll.UpdateManyAsync(filter, update, cancellationToken: cancellationToken);

    public static IFindFluent<T, T> FindMaybe<T>(
        this IMongoCollection<T> coll, IClientSessionHandle session, FilterDefinition<T> filter)
        => session != null
            ? coll.Find(session, filter)
            : coll.Find(filter);

    public static Task<T> FindOneAndDeleteMaybeAsync<T>(
        this IMongoCollection<T> coll, IClientSessionHandle session, FilterDefinition<T> filter,
        FindOneAndDeleteOptions<T, T> options = null, CancellationToken cancellationToken = default)
        => session != null
            ? coll.FindOneAndDeleteAsync(session, filter, options, cancellationToken)
            : coll.FindOneAndDeleteAsync(filter, options, cancellationToken);

    public static Task<DeleteResult> DeleteManyMaybeAsync<T>(
        this IMongoCollection<T> coll, IClientSessionHandle session, FilterDefinition<T> filter,
        CancellationToken cancellationToken = default)
        => session != null
            ? coll.DeleteManyAsync(session, filter, cancellationToken: cancellationToken)
            : coll.DeleteManyAsync(filter, cancellationToken: cancellationToken);
}
