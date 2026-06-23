using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Tharga.MongoDB;

/// <summary>
/// Tracks per-connection reachability for all configured connections. Populated by the startup
/// connectivity pre-check in <see cref="MongoDbRegistrationExtensions.UseMongoDB"/> and
/// re-evaluable live via <see cref="CheckAsync"/>. Registered as a singleton by
/// <c>AddMongoDB</c>.
/// <para>
/// Built on the existing non-throwing <see cref="IMongoDbService.GetInfoAsync"/> /
/// <see cref="DatabaseInfo.CanConnect"/> probe. Use it to drive a readiness/health endpoint; it
/// recovers automatically once connectivity is restored (e.g. once an IP is allow-listed).
/// </para>
/// </summary>
public interface IMongoDbConnectivityState
{
    /// <summary>
    /// True when every configured connection was reachable as of the last check. When no check
    /// has run yet, this is true (nothing has been observed as unreachable).
    /// </summary>
    bool IsHealthy { get; }

    /// <summary>
    /// The most recent per-connection results. Empty until the first check runs.
    /// </summary>
    IReadOnlyList<ConnectionConnectivity> Connections { get; }

    /// <summary>
    /// Re-evaluates connectivity for every configured connection and updates
    /// <see cref="Connections"/> / <see cref="IsHealthy"/>. Never throws — an unreachable
    /// connection is reported as <see cref="ConnectionConnectivity.CanConnect"/> false.
    /// </summary>
    Task<IReadOnlyList<ConnectionConnectivity>> CheckAsync(CancellationToken cancellationToken = default);
}
