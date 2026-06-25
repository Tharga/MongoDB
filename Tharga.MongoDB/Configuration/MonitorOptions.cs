using System;

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

    /// <summary>
    /// When forwarding to a central monitor (<see cref="SendTo"/>), whether to forward every completed call.
    /// This can be a large, continuous stream proportional to database activity, so it is <b>off by default</b>.
    /// Collection metadata and (viewer-gated) queue metrics are forwarded regardless of this setting.
    /// </summary>
    public bool ForwardCompletedCalls { get; set; } = false;

    /// <summary>
    /// How often the agent forwards a queue-state snapshot to the central monitor (only while someone is
    /// watching live). Smaller is smoother on the live graph; larger is less chatter. Default is 1 second.
    /// </summary>
    public TimeSpan QueueMetricInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Enable MongoDB driver command monitoring. When enabled, driver-level command durations
    /// are captured and surfaced in call step data, allowing operators to distinguish slow
    /// server execution from thread pool starvation. Default is false.
    /// Should NOT be always-on in production due to volume.
    /// </summary>
    public bool EnableCommandMonitoring { get; set; }
}