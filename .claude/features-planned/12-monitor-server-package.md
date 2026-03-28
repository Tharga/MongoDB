# Feature: Tharga.MongoDB.Monitor.Server package

## Source
Architecture — distributed monitoring

## Goal
Create a NuGet package that receives monitoring data from remote agents via SignalR and aggregates it with local monitoring data into a unified `IDatabaseMonitor`.

## Scope
- New project `Tharga.MongoDB.Monitor.Server`
- SignalR hub that accepts incoming `CallDto` and related data from clients
- Aggregates remote data into the existing `IDatabaseMonitor` / `ICallLibrary` implementation
- Subscription-based: notifies clients when someone starts/stops watching
- Configurable via:
  ```csharp
  builder.AddMongoDB(o => o.Monitor.ReceiveFromRemote());
  app.MapMongoDbMonitorHub();
  ```
- Blazor components work unchanged — they see local + remote data through the same `IDatabaseMonitor`
- Evaluate whether to use `Tharga.Communication` as transport or a standalone SignalR hub

## Dependencies
- Feature #09 (monitor-source-flag) — needed to distinguish local vs remote data
- Feature #11 (monitor-client-package) — client and server must agree on protocol

## Acceptance Criteria
- [ ] Package builds and publishes independently
- [ ] Server receives and aggregates data from multiple remote agents
- [ ] `IDatabaseMonitor` returns combined local + remote data
- [ ] Blazor components display all sources without code changes
- [ ] Hub handles client connect/disconnect gracefully
- [ ] Tests cover aggregation of multi-source data

## Done Condition
A Blazor server can display monitoring data from its own database and from remote agents in the same dashboard, by adding this package and two lines of configuration.
