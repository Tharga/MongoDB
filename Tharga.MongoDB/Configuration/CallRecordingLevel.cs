namespace Tharga.MongoDB.Configuration;

/// <summary>
/// How much per-call detail the monitor records. Recording is pure overhead when nothing consumes it
/// (a headless agent with forwarding off and no live viewer), so this lets a process pay only for what's used.
/// </summary>
public enum CallRecordingLevel
{
    /// <summary>Always record the full call, including its step timeline. Most detail, most cost.</summary>
    Full,

    /// <summary>
    /// Always record the call (function, collection, timing, counts), but build the detailed step timeline
    /// only while the data is being consumed (forwarding on, or a live viewer). The default — keeps the
    /// recent-call list and aggregates without the cold-start blind spot, while skipping step detail when idle.
    /// </summary>
    OnDemand,

    /// <summary>
    /// Record nothing unless the data is being consumed (forwarding on, or a live viewer). Lowest cost; accepts
    /// that there is no look-back history until a consumer appears. Best for headless agents.
    /// </summary>
    WhenConsumed,
}
