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

- **Calls** — every database operation captured by `IDatabaseMonitor` (filter, sort, latency, exception, explain plan).
- **Collection info** — registered collections, document counts, index status.
- **Queue metrics** — execute-limiter depth and throughput.

The agent appears as a connected client on the server side and can be inspected, addressed, and acted upon from there (e.g. "rebuild index on agent X" via remote action delegation).

## Documentation

Full docs, the matching server package, and the centralised-monitoring topology overview: [github.com/Tharga/MongoDB](https://github.com/Tharga/MongoDB).

[![GitHub repo](https://img.shields.io/github/repo-size/Tharga/MongoDB?style=flat&logo=github&logoColor=red&label=Repo)](https://github.com/Tharga/MongoDB)
