# Monitoring

Every database call (filter, sort, latency, exception, explain plan) is captured by the built-in `IDatabaseMonitor`. The monitor tracks collection metadata such as document counts, sizes, indexes and clean status. By default it persists state to a `_monitor` collection in MongoDB so data survives restarts and is shared across instances.

## Storage modes

| Mode | Behaviour |
|---|---|
| `Database` (default) | Persists to the `_monitor` collection. State survives restarts. |
| `Memory` | In-memory only. State is lost on restart. |

Configure via `appsettings.json`:

```json
"MongoDB": {
  "Monitor": {
    "Enabled": true,
    "StorageMode": "Database",
    "LastCallsToKeep": 1000,
    "SlowCallsToKeep": 200,
    "ForwardCompletedCalls": false,
    "QueueMetricInterval": "00:00:01",
    "ClusterConnectionLimit": 3000
  }
}
```

Or by code via `services.AddMongoDB(o => o.Monitor = new MonitorOptions { ... })`.

| Option | Where | Default | Purpose |
|---|---|---|---|
| `ForwardCompletedCalls` | Agent | `false` | Forward every completed call to the central monitor. Off by default — it is a large, continuous stream proportional to database activity. See [Centralised monitoring](#centralised-monitoring). |
| `QueueMetricInterval` | Agent | `00:00:01` (1s) | How often a queue/connection snapshot is forwarded while someone is watching live. Larger = less chatter, coarser live graph. |
| `ClusterConnectionLimit` | Server | `null` | A cluster's connection limit (e.g. an Atlas tier's max, often 3000). When set, the queue view shows total open connections as a fraction of it. See [Connection-pool usage](#connection-pool-queue-and-in-flight-calls). |

## Source identification

All monitoring data is tagged with a source name. Default: `{MachineName}/{AssemblyName}`. Override via `Monitor.SourceName`. The Blazor call view shows a Source column when calls from multiple sources are present.

## Command monitoring

Enable driver-level command timing to see how much of "Action" time is real MongoDB server time vs thread-pool wait. Disabled by default; enable with `Monitor.EnableCommandMonitoring = true`. Steps then include breakdowns like:

- **FetchCollection**: `Driver: listIndexes 2.10ms | Other: 0.45ms`
- **Action**: `Driver: find 12.34ms | Other: 3.21ms`

Useful for distinguishing slow database from slow serialization or application contention.

## Connection pool, queue and in-flight calls

Database operations pass through a per-pool concurrency limiter (one pool per `MongoClient`, keyed by the set of
server hosts **and** the pool's `MaxConnectionPoolSize`). The Blazor queue view surfaces this **per pool**, not as
a single process-wide figure:

- **Queue / Exec counters and the Queue-Depth / Wait-Time graphs** are drawn one line per pool, labelled by the
  configuration name(s) routing through that pool. Configurations on separate clusters get their own lines;
  configurations sharing a cluster *and* the same `MaxPoolSize` collapse into one line (they share one connection
  pool), while configurations on the same cluster with different pool sizes get their own client and their own
  line. When agents are connected, their pools appear as additional lines (source-suffixed), so local + remote are
  shown together.

- **Connection-pool usage vs a limit.** The monitor counts the *actual* open MongoDB driver connections per
  cluster (from the driver's connection-pool events) — this is what counts toward a cluster's connection limit,
  unlike `Exec`, which is in-use operations only and ignores idle-but-open pooled connections. The queue view
  shows, per cluster across **all sources (this server + every agent)**, the total open connections and the
  total capacity (sum of each pool's `maxPoolSize`). Set `Monitor.ClusterConnectionLimit` (on the server) to your
  cluster's limit (e.g. an Atlas tier's 3000) to see `open / limit` with a bar that warns as you approach it.
  Read it via `IDatabaseMonitor.GetClusterConnectionSummary()`.

  > Only monitored processes are counted — other clients (Compass, un-instrumented services) and the driver's
  > per-process SDAM heartbeat connections are not. Treat the figure as a close lower bound on the cluster total.

- **Inspecting the queue (in-flight calls).** When a flood stacks up behind the limiter, you can see exactly
  *what* is queued vs executing — grouped by collection, function and (rendered on demand) filter — via the
  [MCP monitoring resource](mcp-integration.md) and `IDatabaseMonitor.GetInFlightCalls()`. The Blazor *Ongoing*
  call view shows in-flight calls too; these are tracked separately from the capped recent-call ring so a flood
  no longer evicts the longest-queued calls from view. (In-flight detail is per-process: query an agent's own
  MCP for its queued calls; the central server receives only the per-pool counts from agents.)

## Centralised monitoring

For a single dashboard pane covering many MongoDB-talking services, install [`Tharga.MongoDB.Monitor.Client`](https://www.nuget.org/packages/Tharga.MongoDB.Monitor.Client) on each agent and [`Tharga.MongoDB.Monitor.Server`](https://www.nuget.org/packages/Tharga.MongoDB.Monitor.Server) on the central server.

**Agent:**

```csharp
builder.AddMongoDB();
builder.AddMongoDbMonitorClient(sendTo: "https://monitor-server", apiKey: "...");
```

**Server:**

```csharp
builder.AddMongoDB();
builder.AddMongoDbMonitorServer(primaryApiKey: "...");

var app = builder.Build();
app.UseMongoDbMonitorServer();
```

Agents push monitoring data fire-and-forget over [Tharga.Communication](https://www.nuget.org/packages/Tharga.Communication) (SignalR-backed). The server ingests it into its local `IDatabaseMonitor` so the [Blazor admin UI](https://www.nuget.org/packages/Tharga.MongoDB.Blazor) renders local + remote data side by side. When the server is unavailable or not configured, the agent has zero overhead. By default agents forward collection metadata and (while watched) per-pool queue/connection snapshots; forwarding of every completed call is opt-in via `Monitor.ForwardCompletedCalls` — see [What agents forward](#what-agents-forward-and-when).

## API key rotation

Configure both `primaryApiKey` and `secondaryApiKey` on the server during a rotation window — either is accepted. Agents can also load keys from `appsettings.json` or User Secrets via the `Tharga:Communication:ApiKey` configuration path.

## Remote action delegation

When the server dashboard displays collections from remote agents, actions like *touch*, *drop index*, *restore index*, *clean* are automatically delegated to the agent that owns the collection. No extra configuration — if `Monitor.Client` and `Monitor.Server` are installed, it works out of the box.

## What agents forward, and when

| Data | When forwarded |
|---|---|
| Collection metadata (counts, sizes, indexes, clean status) | Always — a burst at connect, then on change. Small. |
| Queue / connection per-pool snapshots | Only while someone is viewing the live queue tab (gated on an active subscription), at `QueueMetricInterval`. |
| Completed calls | Only when `Monitor.ForwardCompletedCalls = true` (off by default). This is the large stream — leave it off unless you need per-call history on the central server. With it off, the agent's *Last/Slow calls* lists on the server stay empty (its queue/connection metrics and collection metadata still flow). |

Blazor components subscribe to the live data on mount and unsubscribe on dispose, so queue/connection snapshots stop when no one is looking. The server signals each agent to start/stop forwarding via an explicit message (`SetLiveMonitoringActiveMessage`); the agent gates its queue-metric timer on that signal. Each agent reports its own forwarding configuration (call forwarding on/off, queue interval, storage mode) on connect; the **Clients** page shows it per agent (a *Call forwarding* badge, with the full set in the client detail dialog).

You can also drive and observe this live flow **headlessly** — without opening the Queue view in a browser — via the [MCP monitoring tools](mcp-integration.md#live-monitoring-diagnostics) (`hold_live_subscription`, `get_per_pool_queue_state`, `get_monitor_clients`, `get_client_communication`). Useful for verifying an agent is forwarding queue metrics from a script or an AI agent.

## Reset

`IDatabaseMonitor.ResetAsync()` clears all cached state (in-memory + persisted). The Blazor `CollectionView` exposes a Reset button that calls this.

## CollectionView at scale

For deployments with thousands of collections, the `CollectionView` uses a per-process stale-while-revalidate cache: first navigation per host pays the full load, every subsequent navigation across all admin users on that host is instant. After the synchronous render, a 16-way concurrency-capped background revalidator refreshes each row from MongoDB (visible page first, off-screen rows after). Rows currently being refreshed render with a blue background (`var(--rz-info-light)`) so it's clear which values are stale-but-loading. Cache is in-memory only and does not survive host restart.

## See also

- [API: IDatabaseMonitor](xref:Tharga.MongoDB.IDatabaseMonitor)
- [API: MonitorOptions](xref:Tharga.MongoDB.Configuration.MonitorOptions)
- [Blazor admin UI components](https://www.nuget.org/packages/Tharga.MongoDB.Blazor)
