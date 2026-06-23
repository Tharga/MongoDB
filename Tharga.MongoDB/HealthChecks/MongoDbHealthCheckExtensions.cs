using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Collections.Generic;
using Tharga.MongoDB;
using Tharga.MongoDB.HealthChecks;

// ReSharper disable once CheckNamespace -- placed in the DI namespace so AddMongoDb() surfaces
// next to AddHealthChecks()/AddCheck() without an extra using.
namespace Microsoft.Extensions.DependencyInjection;

public static class MongoDbHealthCheckExtensions
{
    /// <summary>
    /// Adds a health check that reports the reachability of every configured MongoDB connection,
    /// built on <see cref="IMongoDbConnectivityState"/>. Opt-in:
    /// <c>services.AddHealthChecks().AddMongoDb()</c>. Requires that <c>AddMongoDB</c> has been
    /// called so the connectivity state is registered.
    /// </summary>
    /// <param name="builder">The health-checks builder.</param>
    /// <param name="name">The health-check registration name. Default <c>"mongodb"</c>.</param>
    /// <param name="live">
    /// When true (default), the check re-probes connectivity on each call so a degraded app
    /// recovers as soon as the database is reachable again. When false, reports the last known
    /// state from the startup pre-check without re-probing.
    /// </param>
    /// <param name="failureStatus">
    /// The status reported when a connection is unreachable. Default (null) is
    /// <see cref="HealthStatus.Unhealthy"/>.
    /// </param>
    /// <param name="tags">Optional tags for filtering (e.g. <c>"ready"</c> for readiness probes).</param>
    public static IHealthChecksBuilder AddMongoDb(
        this IHealthChecksBuilder builder,
        string name = "mongodb",
        bool live = true,
        HealthStatus? failureStatus = null,
        IEnumerable<string> tags = null)
    {
        return builder.Add(new HealthCheckRegistration(
            name,
            sp => new MongoDbHealthCheck(sp.GetRequiredService<IMongoDbConnectivityState>(), live),
            failureStatus,
            tags));
    }
}
