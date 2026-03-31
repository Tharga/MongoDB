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

    /// <summary>
    /// Controls where monitor state is persisted. Database stores state in the _monitor
    /// collection so it survives restarts and is shared across multiple application instances.
    /// Default is Database.
    /// </summary>
    public MonitorStorageMode StorageMode { get; set; } = MonitorStorageMode.Database;

    /// <summary>
    /// Identifies the source of monitoring data. Used to distinguish data from different
    /// applications or agents in a distributed monitoring scenario.
    /// Defaults to "{MachineName}/{EntryAssemblyName}" when not configured.
    /// </summary>
    public string SourceName { get; set; }

    /// <summary>
    /// URL of the central monitor server to forward monitoring data to.
    /// When set, the Tharga.MongoDB.Monitor.Client package must be referenced
    /// and <see cref="SendTo"/> is used as the Tharga.Communication server address.
    /// When null or empty, no forwarding is configured.
    /// </summary>
    public string SendTo { get; set; }
}