using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Tharga.MongoDB.HealthChecks;

/// <summary>
/// An <see cref="IHealthCheck"/> that reports the reachability of every configured MongoDB
/// connection, built on <see cref="IMongoDbConnectivityState"/>. Register via
/// <c>services.AddHealthChecks().AddMongoDb()</c>. Reports unhealthy while any connection is
/// unreachable and recovers automatically once connectivity is restored.
/// </summary>
public sealed class MongoDbHealthCheck : IHealthCheck
{
    private readonly IMongoDbConnectivityState _state;
    private readonly bool _live;

    /// <param name="state">The connectivity state surface.</param>
    /// <param name="live">
    /// When true (default for the helper), the check re-probes connectivity on each call so it
    /// recovers as soon as the database becomes reachable. When false, it reports the last known
    /// state captured by the startup pre-check (cheaper, but only refreshed by other probes).
    /// </param>
    public MongoDbHealthCheck(IMongoDbConnectivityState state, bool live = true)
    {
        _state = state;
        _live = live;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var connections = _live
            ? await _state.CheckAsync(cancellationToken)
            : _state.Connections;

        var data = connections.ToDictionary(
            c => c.ConfigurationName,
            c => (object)(c.CanConnect ? "reachable" : c.Message ?? "unreachable"));

        var unreachable = connections.Where(c => !c.CanConnect).ToArray();
        if (unreachable.Length == 0)
            return HealthCheckResult.Healthy("All configured MongoDB connections are reachable.", data);

        var description = $"{unreachable.Length} MongoDB connection(s) unreachable: " +
            string.Join("; ", unreachable.Select(c => $"{c.ConfigurationName}: {c.Message}"));

        return new HealthCheckResult(context.Registration.FailureStatus, description, exception: null, data: data);
    }
}
