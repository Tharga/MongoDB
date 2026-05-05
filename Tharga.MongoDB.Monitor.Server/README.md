# Tharga.MongoDB.Monitor.Server

Receives MongoDB monitoring data from one or more remote agents running [`Tharga.MongoDB.Monitor.Client`](https://www.nuget.org/packages/Tharga.MongoDB.Monitor.Client) and aggregates it into the local `IDatabaseMonitor` — so the host app's [`Tharga.MongoDB.Blazor`](https://www.nuget.org/packages/Tharga.MongoDB.Blazor) admin UI can show calls, collections, and clients across every connected service from a single pane. Built on [Tharga.Communication](https://www.nuget.org/packages/Tharga.Communication) (SignalR-backed).

## Install

```
dotnet add package Tharga.MongoDB.Monitor.Server
```

```csharp
builder.AddMongoDB();                              // local MongoDB usage (and the IDatabaseMonitor everything aggregates into)
builder.AddMongoDbMonitorServer(
    primaryApiKey: cfg["MongoMonitor:PrimaryApiKey"],     // optional — when set, agents must match
    secondaryApiKey: cfg["MongoMonitor:SecondaryApiKey"]); // optional — for zero-downtime key rotation

var app = builder.Build();
app.UseMongoDbMonitorServer();    // maps the SignalR hub at "/hub" by default
```

Drop the Blazor admin components onto a page (see [`Tharga.MongoDB.Blazor`](https://www.nuget.org/packages/Tharga.MongoDB.Blazor)) and remote agents show up alongside the local app's own data.

## What it does

- **Receives** call, collection-info, and queue-metric messages from connected agents.
- **Tracks** each agent's connection state via `MonitorClientStateService` and `MonitorClientRepository` — the admin UI's `<ClientsView />` lists them.
- **Bridges** agent connections into `IDatabaseMonitor` via `MonitorClientBridge`, so the calls/collections views render remote data alongside local data.
- **Delegates remote actions** — admin tools can target a specific agent (e.g. *touch this collection on agent X*, *rebuild this index on agent Y*) through `IRemoteActionDispatcher`.
- **Live monitoring subscriptions** — push live updates to subscribed clients via `ILiveMonitoringSubscription`.

## Documentation

Full docs, the matching client package, and the centralised-monitoring topology overview: [github.com/Tharga/MongoDB](https://github.com/Tharga/MongoDB).

[![GitHub repo](https://img.shields.io/github/repo-size/Tharga/MongoDB?style=flat&logo=github&logoColor=red&label=Repo)](https://github.com/Tharga/MongoDB)
