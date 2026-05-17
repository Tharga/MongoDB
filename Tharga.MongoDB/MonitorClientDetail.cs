using System.Collections.Generic;

namespace Tharga.MongoDB;

/// <summary>
/// Snapshot of what a remote monitoring agent has contributed to the aggregator:
/// identity, collections it has reported, its recent calls, and its latest queue
/// state. Returned by <see cref="IDatabaseMonitor.GetMonitorClientDetail"/> for
/// the per-agent detail dialog in Tharga.MongoDB.Blazor's <c>ClientsView</c>.
/// </summary>
public record MonitorClientDetail
{
    public required MonitorClientDto Client { get; init; }
    public required IReadOnlyCollection<string> CollectionKeys { get; init; }
    public required IReadOnlyCollection<CallInfo> RecentCalls { get; init; }

    /// <summary>
    /// Latest queue snapshot reported by the agent, or <c>null</c> if the agent
    /// hasn't sent a queue metric yet (e.g. just connected, or the agent's local
    /// queue has been idle so the forwarder hasn't emitted anything).
    /// </summary>
    public ConnectionPoolStateDto QueueState { get; init; }
}
