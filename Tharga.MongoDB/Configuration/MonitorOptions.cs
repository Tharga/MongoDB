namespace Tharga.MongoDB.Configuration;

public record MonitorOptions
{
    /// <summary>
    /// Enable or disable the MongoDB monitor.
    /// By default, it is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Number of calls to keep. The latest calls replaces the oldest.
    /// </summary>
    public int LastCallsToKeep { get; set; } = 1000;

    /// <summary>
    /// Number of slow calls to keep. The slowest calls replaces the less slow calls.
    /// </summary>
    public int SlowCallsToKeep { get; set; } = 200;
}