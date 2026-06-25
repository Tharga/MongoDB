# Tharga.MongoDB.Monitor.Client

Forwards [`Tharga.MongoDB`](https://www.nuget.org/packages/Tharga.MongoDB) monitoring data — calls, collection info, queue metrics — from a running app (the *agent*) to a central [`Tharga.MongoDB.Monitor.Server`](https://www.nuget.org/packages/Tharga.MongoDB.Monitor.Server) over [Tharga.Communication](https://www.nuget.org/packages/Tharga.Communication) (SignalR-backed). Use this when you want a single dashboard pane covering many MongoDB-talking services without each one shipping its own admin UI.

## Install

```
dotnet add package Tharga.MongoDB.Monitor.Client
```

```csharp
builder.AddMongoDB();                                          // the agent's normal MongoDB usage
builder.AddMongoDbMonitorClient(
    sendTo: "https://monitor.example.com/",                    // monitor server URL
    apiKey: builder.Configuration["MongoMonitor:ApiKey"]);     // optional, must match one of the server's primary/secondary keys
```

For hosts that only expose `IServiceCollection` at registration time — for example a `Tharga.Wpf`-based agent whose `App.Register(HostBuilderContext context, IServiceCollection services)` callback has no builder in scope — use the `IServiceCollection` overload instead:

```csharp
services.AddMongoDbMonitorClient(
    configuration: context.Configuration,
    sendTo: "https://monitor.example.com/",
    apiKey: context.Configuration["MongoMonitor:ApiKey"]);
```

If `sendTo` is null/empty the client is a no-op — convenient for local dev. Both overloads behave identically once `sendTo` is set.

## What it sends

- **Collection info** — registered collections, document counts, index status. Always forwarded (small).
- **Queue & connection metrics** — per-pool execute-limiter depth/throughput and actual open-connection counts. Forwarded only while someone is viewing the live tab on the server, at `Monitor.QueueMetricInterval` (default 1s).
- **Calls** — every completed database operation (filter, sort, latency, exception, explain plan). **Opt-in**, off by default — set `Monitor.ForwardCompletedCalls = true`. This is a large, continuous stream proportional to database activity, so enable it only when you want full per-call history on the central server.

These are configured on the agent's `Monitor` options (same `MongoDB:Monitor` section as the core package):

```json
"MongoDB": {
  "Monitor": {
    "ForwardCompletedCalls": false,
    "QueueMetricInterval": "00:00:01"
  }
}
```

The agent appears as a connected client on the server side — its forwarding configuration (call forwarding on/off, queue interval, storage mode) is shown on the server's **Clients** page — and it can be inspected, addressed, and acted upon from there (e.g. "rebuild index on agent X" via remote action delegation).

## Documentation

Full docs, the matching server package, and the centralised-monitoring topology overview: [github.com/Tharga/MongoDB](https://github.com/Tharga/MongoDB).

[![GitHub repo](https://img.shields.io/github/repo-size/Tharga/MongoDB?style=flat&logo=github&logoColor=red&label=Repo)](https://github.com/Tharga/MongoDB)
