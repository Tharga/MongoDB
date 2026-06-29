namespace Tharga.MongoDB.Monitor.Client;

/// <summary>
/// Sent from the central monitor server to agents to turn live monitoring (queue-metric
/// forwarding) on or off. This is an explicit, fully server-controlled signal that replaces
/// reliance on Tharga.Communication's built-in subscription-state propagation, which has proven
/// unreliable at reaching agents in some deployments. The server sends <see cref="Active"/> = true
/// when a live subscriber is present (Queue view open) and false when the last one leaves.
/// </summary>
public record SetLiveMonitoringActiveMessage
{
    /// <summary>Whether at least one live-monitoring subscriber is currently active on the server.</summary>
    public required bool Active { get; init; }
}
