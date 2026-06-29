using System;
using System.Collections.Generic;

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
    /// A single connection limit applied to <b>every</b> cluster that <see cref="ClusterConnectionLimitResolver"/>
    /// does not resolve. Only useful when every monitored cluster shares the same limit; for mixed deployments
    /// (e.g. localhost + Atlas, or different Atlas tiers) leave this null and use the resolver instead.
    /// When neither yields a value, the cluster shows its open-connection total with no limit bar.
    /// </summary>
    public int? ClusterConnectionLimit { get; set; }

    /// <summary>
    /// Resolves the connection limit <b>per cluster</b> (e.g. each Atlas tier's max, or "unknown" for a
    /// self-hosted node). Return the limit, or <c>null</c> to fall back to <see cref="ClusterConnectionLimit"/>
    /// and then to "no limit". Mirrors <see cref="DatabaseOptions.MaxPoolSizeOverride"/>: the
    /// <see cref="IServiceProvider"/> is passed in, so the delegate can read a service that an external feature
    /// (e.g. an Atlas-API poller) updates at runtime. Called on the monitor render path, so it must be fast and
    /// non-blocking — read a cached value, do not perform I/O here.
    /// <code>
    /// o.Monitor.ClusterConnectionLimitResolver = (sp, ctx) => ctx.IsAtlas
    ///     ? sp.GetRequiredService&lt;IMyTierCache&gt;().LimitFor(ctx.Cluster) // runtime/external value
    ///     : null;                                                          // self-hosted: no known limit
    /// </code>
    /// </summary>
    public Func<IServiceProvider, ClusterConnectionLimitContext, int?> ClusterConnectionLimitResolver { get; set; }

    /// <summary>
    /// Enable MongoDB driver command monitoring. When enabled, driver-level command durations
    /// are captured and surfaced in call step data, allowing operators to distinguish slow
    /// server execution from thread pool starvation. Default is false.
    /// Should NOT be always-on in production due to volume.
    /// </summary>
    public bool EnableCommandMonitoring { get; set; }

    /// <summary>
    /// How much per-call data to record (see <see cref="CallRecordingLevel"/>). Recording is wasted work when
    /// nothing consumes it, so the default <see cref="CallRecordingLevel.OnDemand"/> keeps the lightweight call
    /// record always but builds the step timeline only while forwarding is on or a live viewer is attached. A
    /// headless agent can drop to <see cref="CallRecordingLevel.WhenConsumed"/> to record nothing while idle.
    /// </summary>
    public CallRecordingLevel CallRecordingLevel { get; set; } = CallRecordingLevel.OnDemand;
}

/// <summary>
/// The cluster a <see cref="MonitorOptions.ClusterConnectionLimitResolver"/> is being asked about.
/// </summary>
public sealed record ClusterConnectionLimitContext
{
    /// <summary>The cluster identity — the server host(s) connected to, e.g. <c>localhost:27017</c> or <c>cluster0.ab12.mongodb.net</c>.</summary>
    public required string Cluster { get; init; }

    /// <summary>True when the cluster looks like an Atlas deployment (host on <c>mongodb.net</c>); false for self-hosted / unknown.</summary>
    public required bool IsAtlas { get; init; }

    /// <summary>The configuration name(s) that route through this cluster.</summary>
    public required IReadOnlyCollection<string> ConfigurationNames { get; init; }
}