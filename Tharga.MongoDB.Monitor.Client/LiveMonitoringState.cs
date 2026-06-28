namespace Tharga.MongoDB.Monitor.Client;

/// <summary>
/// Process-wide flag tracking whether the central monitor server has an active live-monitoring
/// subscriber. Set by <see cref="SetLiveMonitoringActiveHandler"/> from server-pushed
/// <see cref="SetLiveMonitoringActiveMessage"/> messages and read by <see cref="MonitorForwarder"/>
/// to gate queue-metric forwarding. This is the explicit signal that replaces the unreliable
/// <c>HasSubscribers&lt;LiveMonitoringMarker&gt;()</c> dependency.
/// </summary>
internal sealed class LiveMonitoringState
{
    private volatile bool _active;

    /// <summary>Whether the server currently has a live-monitoring subscriber.</summary>
    public bool Active
    {
        get => _active;
        set => _active = value;
    }
}
