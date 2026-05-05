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
    "SlowCallsToKeep": 200
  }
}
```

Or by code via `services.AddMongoDB(o => o.Monitor = new MonitorOptions { ... })`.

## Source identification

All monitoring data is tagged with a source name. Default: `{MachineName}/{AssemblyName}`. Override via `Monitor.SourceName`. The Blazor call view shows a Source column when calls from multiple sources are present.

## Command monitoring

Enable driver-level command timing to see how much of "Action" time is real MongoDB server time vs thread-pool wait. Disabled by default; enable with `Monitor.EnableCommandMonitoring = true`. Steps then include breakdowns like:

- **FetchCollection**: `Driver: listIndexes 2.10ms | Other: 0.45ms`
- **Action**: `Driver: find 12.34ms | Other: 3.21ms`

Useful for distinguishing slow database from slow serialization or application contention.

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

Agents push call events fire-and-forget over [Tharga.Communication](https://www.nuget.org/packages/Tharga.Communication) (SignalR-backed). The server ingests them into its local `IDatabaseMonitor` so the [Blazor admin UI](https://www.nuget.org/packages/Tharga.MongoDB.Blazor) renders local + remote data side by side. When the server is unavailable or not configured, the agent has zero overhead.

## API key rotation

Configure both `primaryApiKey` and `secondaryApiKey` on the server during a rotation window — either is accepted. Agents can also load keys from `appsettings.json` or User Secrets via the `Tharga:Communication:ApiKey` configuration path.

## Remote action delegation

When the server dashboard displays collections from remote agents, actions like *touch*, *drop index*, *restore index*, *clean* are automatically delegated to the agent that owns the collection. No extra configuration — if `Monitor.Client` and `Monitor.Server` are installed, it works out of the box.

## Live subscriptions

Live data (queue depth, ongoing calls) is only forwarded when someone is actively viewing the relevant tab. Blazor components subscribe on mount and unsubscribe on dispose. Collection metadata and completed calls are always forwarded.

## Reset

`IDatabaseMonitor.ResetAsync()` clears all cached state (in-memory + persisted). The Blazor `CollectionView` exposes a Reset button that calls this.

## See also

- [API: IDatabaseMonitor](xref:Tharga.MongoDB.IDatabaseMonitor)
- [API: MonitorOptions](xref:Tharga.MongoDB.Configuration.MonitorOptions)
- [Blazor admin UI components](https://www.nuget.org/packages/Tharga.MongoDB.Blazor)
