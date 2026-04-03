namespace Tharga.MongoDB.Monitor.Client;

/// <summary>
/// Marker type used as the subscription topic for live monitoring data
/// (ongoing calls, queue metrics). Agents only send live data when
/// the server has active subscribers for this type.
/// </summary>
public record LiveMonitoringMarker;
