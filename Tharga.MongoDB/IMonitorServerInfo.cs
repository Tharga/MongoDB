namespace Tharga.MongoDB;

/// <summary>
/// Exposes information about the monitor server host for display in the UI. Registered by
/// <c>Tharga.MongoDB.Monitor.Server</c> when installed; absent when the dashboard runs without the
/// server package, so consumers should resolve it optionally.
/// </summary>
public interface IMonitorServerInfo
{
    /// <summary>The <c>Tharga.MongoDB.Monitor.Server</c> library version running on this host.</summary>
    string LibraryVersion { get; }
}
