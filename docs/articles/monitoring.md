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
| `CallRecordingLevel` | Any | `OnDemand` | How much per-call data to record. `Full` always records calls + step timeline; `OnDemand` (default) records the lightweight call always but builds the step timeline only while consumed (forwarding on or a live viewer); `WhenConsumed` records nothing until consumed (best for headless agents). The monitor server always counts as a consumer. |
| `EnableCommandMonitoring` | Any | `false` | Capture driver command durations (the `Driver: … \| Other: …` breakdown on call steps). The listener is always subscribed, so capture can be toggled at runtime — locally (`IDatabaseMonitor.SetCommandMonitoring`) or per-agent from the Clients dialog (`SetClientCommandMonitoringAsync`). This is the startup default. |
| `EnableActivitySource` | Any | `true` | Emit a dependency span per driver command on the `Tharga.MongoDB` activity source. Costs nothing until something listens, so it is on by default. See [Dependency spans](#dependency-spans-distributed-tracing). |
| `CaptureCommandText` | Any | `false` | Attach the command document to each span as `db.statement`. Off by default — the command carries query values, i.e. data. |
| `ClusterConnectionLimit` | Server | `null` | A single connection limit applied to **every** cluster the resolver doesn't handle. Use only when all clusters share one limit; otherwise leave null and use the resolver. |
| `ClusterConnectionLimitResolver` | Server | store-backed | `Func<IServiceProvider, ClusterConnectionLimitContext, int?>` — resolves the limit **per cluster** (e.g. each Atlas tier's max, or `null` for self-hosted). Mirrors `MaxPoolSizeOverride`; the `IServiceProvider` lets it read a value an external feature updates at runtime. Called on the render path, so it must be fast (read a cached value, no I/O). The context carries the cluster host, an `IsAtlas` flag (host on `mongodb.net`), and the config names. Falls back to `ClusterConnectionLimit`, then to no bar. **When unset, defaults to the editable config store** (below). |

When you don't supply a resolver, limits come from a per-cluster config store (`IClusterConfigStore`, persisted in a `_monitorConfig` collection) that the `<ClusterConfigView />` Blazor component edits at runtime: per cluster you set an **Atlas tier** (auto-fills the known limit) or a **manual limit**, a **display alias**, and **warn/danger thresholds** for the bar. To drive limits from an external source instead (e.g. an Atlas-API poller), set `ClusterConnectionLimitResolver` to read your own cached value. See [Connection-pool usage](#connection-pool-queue-and-in-flight-calls).

## Source identification

All monitoring data is tagged with a source name. Default: `{MachineName}/{AssemblyName}`. Override via `Monitor.SourceName`. The Blazor call view shows a Source column when calls from multiple sources are present.

## Command monitoring

Enable driver-level command timing to see how much of "Action" time is real MongoDB server time vs thread-pool wait. Disabled by default; enable with `Monitor.EnableCommandMonitoring = true`. Steps then include breakdowns like:

- **FetchCollection**: `Driver: listIndexes 2.10ms | Other: 0.45ms`
- **Action**: `Driver: find 12.34ms | Other: 3.21ms`

Useful for distinguishing slow database from slow serialization or application contention.

## Dependency spans (distributed tracing)

Command monitoring answers *how long do queries take*. A dependency span answers a different question: *which
call, inside which request, spent the time*. Aggregate durations have already discarded that correlation, so
the two are not alternatives — a trace is what lets you see that one HTTP request spent 14 of its 15 seconds
in a single query.

Database calls are published as `Activity` spans on the **`Tharga.MongoDB`** activity source. Register it with
your tracer provider:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddSource(MongoDbDiagnostics.ActivitySourceName)   // "Tharga.MongoDB"
        .AddAzureMonitorTraceExporter());
```

The constant lives in `Tharga.MongoDB.Diagnostics.MongoDbDiagnostics`, so the name does not have to be typed
out. In Application Insights the spans arrive in the `dependencies` table, nested under the request that made
them.

### What a span carries

One `Client`-kind span per driver command, named `{command} {database}.{collection}` (or `{command}
{database}` for a database-level command such as `dbStats`).

| Tag | Example |
|---|---|
| `db.system` | `mongodb` |
| `db.name` | `MyDatabase` |
| `db.operation` | `find` |
| `db.mongodb.collection` | `user` — absent on database-level commands |
| `server.address` / `server.port` | `cluster0.ab12.mongodb.net` / `27017` |
| `db.statement` | the command document — only when `CaptureCommandText` is on |

A failed command sets the span status to `Error` with the driver's message, plus an `error.type` tag naming
the exception type.

Handshake and heartbeat commands (`hello`, `isMaster`, `buildInfo`, `ping`, `saslStart`, `saslContinue`,
`authenticate`, `getLastError`, `endSessions`) are never reported. They are per-connection chatter rather than
work anyone asked for, and they would drown the trace.

### Why this one is on by default

Unlike `EnableCommandMonitoring`, which buffers entries whether or not anything reads them, span emission does
no work at all until a listener subscribes to the source — the emitter checks first, and allocates nothing
when nobody is there (asserted by measurement in the test suite). An application that never calls
`AddSource("Tharga.MongoDB")` therefore pays a volatile read per command.

Spans are per-command and high-volume, so let your OpenTelemetry pipeline's sampler decide how many are kept
rather than switching the source off and on.

Set `Monitor.EnableActivitySource = false` when you attach your own activity-producing subscriber through
[`ConfigureCluster`](#attaching-your-own-driver-subscriber) — otherwise every call produces two spans.

## Attaching your own driver subscriber

`ConfigureCluster` hands you the driver's `ClusterBuilder` when a `MongoClient` is created, which is the full
driver event stream — command, connection-pool, server-selection, SDAM.

```csharp
services.AddMongoDB(configuration, o =>
{
    o.ConfigureCluster(cb => cb.Subscribe(new MyEventSubscriber()));
});
```

Callbacks are **additive**. The built-in command, connection-pool and span subscriptions are applied first,
then each registered callback in registration order, so a hook cannot switch the monitor off by accident. Call
`ConfigureCluster` more than once to register several.

## Connection pool, queue and in-flight calls

Database operations pass through a per-pool concurrency limiter (one pool per `MongoClient`, keyed by the set of
server hosts **and** the pool's `MaxConnectionPoolSize`). The queue is unbounded unless you bound it — see
[Backpressure](#backpressure) below, which matters more than it sounds. The Blazor queue view surfaces this
**per pool**, not as a single process-wide figure:

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
  Read it via `IDatabaseMonitor.GetClusterConnectionSummary()`, which returns a three-level breakdown that keeps
  the dimensions distinct: **cluster** (the host you connect to — the grouping and the summed total) →
  **pool** (`ClusterPoolSummary`, one per server-key; a cluster carries more than one only when configurations
  differ in max pool size) → **source** (`ClusterPoolSourceConnections`, one process — the single client/server
  item). The cluster's `OpenConnections` is the sum across every pool and source.

  > Only monitored processes are counted — other clients (Compass, un-instrumented services) and the driver's
  > per-process SDAM heartbeat connections are not. Treat the figure as a close lower bound on the cluster total.

- **Inspecting the queue (in-flight calls).** When a flood stacks up behind the limiter, you can see exactly
  *what* is queued vs executing — grouped by collection, function and (rendered on demand) filter — via the
  [MCP monitoring resource](mcp-integration.md) and `IDatabaseMonitor.GetInFlightCalls()`. The Blazor *Ongoing*
  call view shows in-flight calls too; these are tracked separately from the capped recent-call ring so a flood
  no longer evicts the longest-queued calls from view. (In-flight detail is per-process: query an agent's own
  MCP for its queued calls; the central server receives only the per-pool counts from agents.)

### Backpressure

**By default the wait queue has no bound**, and every waiter pins its async state machine and captured data.
A burst of work faster than the pool drains is therefore a memory event rather than a throttling one: one
reported production incident reached ~11,900 queued operations and a ~6 GB heap, and the host killed the
process repeatedly for about three hours until the backlog cleared.

Two opt-in bounds, both `null` by default:

| Option | Effect |
|---|---|
| `Limiter.MaxQueueLength` | The greatest number of operations **waiting** for a slot on one pool. Beyond it, `ExecuteLimiterQueueFullException` instead of a queue slot. |
| `Limiter.QueueTimeout` | The longest one operation may wait before `ExecuteLimiterQueueTimeoutException`. |

```json
"MongoDB": {
  "Limiter": {
    "MaxQueueLength": 5000,
    "QueueTimeout": "00:00:30"
  }
}
```

Catch the base `ExecuteLimiterException` to shed or retry regardless of which bound was hit; both carry the
`ServerKey` of the pool involved.

**Only operations that actually wait count against `MaxQueueLength`.** One that finds a free slot executes
immediately whatever the limit is, so `0` means "never queue", not "never run". And `QueueTimeout` on its own
is not a bound — under a sustained flood `rate × timeout` operations still accumulate — so use it alongside a
length, not instead of one.

Rejections are counted per pool as `PoolQueueState.RejectedCount` on `IQueueMonitor.GetPerPoolState()`. Queue
depth alone never reveals shedding, which is why the count exists. The first rejection of each pressure
episode is logged at Warning and the pool re-arms once its queue drains through work — one line per episode,
because a flood is exactly the case where one line per rejection would reproduce the original incident inside
the logging pipeline.

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

The Clients page shows two distinct versions per agent: **Version** is the agent host application's version (from the Tharga.Communication handshake), and **Library** is the `Tharga.MongoDB.Monitor.Client` package version the agent runs (reported on connect). The dashboard's own `Tharga.MongoDB.Monitor.Server` version is shown above the Clients grid (when the server package is installed).

You can also drive and observe this live flow **headlessly** — without opening the Queue view in a browser — via the [MCP monitoring tools](mcp-integration.md#live-monitoring-diagnostics) (`hold_live_subscription`, `get_per_pool_queue_state`, `get_monitor_clients`, `get_client_communication`). Useful for verifying an agent is forwarding queue metrics from a script or an AI agent.

## Reset

`IDatabaseMonitor.ResetAsync()` clears all cached state (in-memory + persisted). The Blazor `CollectionView` exposes a Reset button that calls this.

## CollectionView at scale

For deployments with thousands of collections, the `CollectionView` uses a per-process stale-while-revalidate cache: first navigation per host pays the full load, every subsequent navigation across all admin users on that host is instant. After the synchronous render, a 16-way concurrency-capped background revalidator refreshes each row from MongoDB (visible page first, off-screen rows after). Rows currently being refreshed render with a blue background (`var(--rz-info-light)`) so it's clear which values are stale-but-loading. Cache is in-memory only and does not survive host restart.

## See also

- [API: IDatabaseMonitor](xref:Tharga.MongoDB.IDatabaseMonitor)
- [API: MonitorOptions](xref:Tharga.MongoDB.Configuration.MonitorOptions)
- [Blazor admin UI components](https://www.nuget.org/packages/Tharga.MongoDB.Blazor)
