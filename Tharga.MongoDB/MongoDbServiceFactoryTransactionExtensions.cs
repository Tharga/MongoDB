using System;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Driver;
using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB;

/// <summary>
/// Transaction extensions on <see cref="IMongoDbServiceFactory"/>. Opens a client session on the configured
/// cluster, runs <c>body</c> inside a transaction, commits on success and aborts on exception.
/// </summary>
public static class MongoDbServiceFactoryTransactionExtensions
{
    /// <summary>
    /// Open a session on the cluster identified by <paramref name="configurationName"/> (default if null), run
    /// <paramref name="body"/> inside a MongoDB transaction. Commits on success, aborts on exception. The driver
    /// retries on transient transaction errors.
    /// </summary>
    /// <remarks>
    /// MongoDB requires a replica set or sharded cluster for transactions; standalone deployments throw.
    /// </remarks>
    public static async Task WithTransactionAsync(
        this IMongoDbServiceFactory factory,
        Func<IClientSessionHandle, CancellationToken, Task> body,
        string configurationName = null,
        TransactionOptions options = null,
        CancellationToken cancellationToken = default)
    {
        if (factory == null) throw new ArgumentNullException(nameof(factory));
        if (body == null) throw new ArgumentNullException(nameof(body));

        var service = factory.GetMongoDbService(() => new DatabaseContext { ConfigurationName = configurationName });
        using var session = await service.StartSessionAsync(cancellationToken: cancellationToken);
        await session.WithTransactionAsync(async (s, ct) =>
        {
            await body(s, ct);
            return true; // dummy
        }, options, cancellationToken);
    }

    /// <summary>
    /// Generic-result variant of <see cref="WithTransactionAsync(IMongoDbServiceFactory, Func{IClientSessionHandle, CancellationToken, Task}, string, TransactionOptions, CancellationToken)"/>.
    /// The body's return value is propagated to the caller on commit.
    /// </summary>
    public static async Task<TResult> WithTransactionAsync<TResult>(
        this IMongoDbServiceFactory factory,
        Func<IClientSessionHandle, CancellationToken, Task<TResult>> body,
        string configurationName = null,
        TransactionOptions options = null,
        CancellationToken cancellationToken = default)
    {
        if (factory == null) throw new ArgumentNullException(nameof(factory));
        if (body == null) throw new ArgumentNullException(nameof(body));

        var service = factory.GetMongoDbService(() => new DatabaseContext { ConfigurationName = configurationName });
        using var session = await service.StartSessionAsync(cancellationToken: cancellationToken);
        return await session.WithTransactionAsync(async (s, ct) => await body(s, ct), options, cancellationToken);
    }
}
