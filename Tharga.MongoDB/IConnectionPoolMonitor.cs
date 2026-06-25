using System.Collections.Generic;

namespace Tharga.MongoDB;

/// <summary>
/// Tracks the actual MongoDB driver connection pool size per cluster (server-key) for this process —
/// the real open-connection count that counts toward a cluster's connection limit (e.g. Atlas max connections).
/// Fed by driver connection-pool events; far more accurate than the limiter's in-use count, which ignores
/// idle-but-open pooled connections.
/// </summary>
public interface IConnectionPoolMonitor
{
    void OnConnectionCreated(string serverKey);
    void OnConnectionClosed(string serverKey);
    void SetMaxPoolSize(string serverKey, int maxPoolSize);
    IReadOnlyList<ConnectionPoolCount> GetSnapshot();
}

/// <summary>Open-connection count and configured ceiling for one cluster pool in this process.</summary>
public record ConnectionPoolCount
{
    public required string ServerKey { get; init; }
    public required int OpenConnections { get; init; }
    public required int MaxPoolSize { get; init; }
}
