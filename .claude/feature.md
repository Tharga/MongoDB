# Feature: Tharga.MongoDB.Monitor.Server package

## Source
Architecture — distributed monitoring (feature #12)

## Originating Branch
develop

## Goal
Create a NuGet package that receives monitoring data from remote agents via Tharga.Communication and aggregates it with local monitoring data into the unified `IDatabaseMonitor`.

## Scope
- New project `Tharga.MongoDB.Monitor.Server`
- References `Tharga.MongoDB.Monitor.Client` (for message types) and `Tharga.Communication` (for server API)
- `PostMessageHandlerBase<MonitorCallMessage>` handler that receives calls from remote agents
- `IngestCall(CallDto)` method on `IDatabaseMonitor` so external calls feed into the existing pipeline
- Simple client state service and repository defaults for monitor connections
- Configurable via `builder.AddMongoDbMonitorServer()`
- Blazor components work unchanged — they see local + remote data through `IDatabaseMonitor`

## Acceptance Criteria
- [ ] New project builds and is included in solution
- [ ] Handler receives `MonitorCallMessage` and injects into `IDatabaseMonitor`
- [ ] Ingested calls appear in Blazor CallView alongside local calls
- [ ] `AddMongoDbMonitorServer()` extension for registration
- [ ] Hub endpoint mapped via `UseMongoDbMonitorServer()`
- [ ] Tests cover: call ingestion, handler wiring
- [ ] Existing tests still pass

## Done Condition
A Blazor server can display monitoring data from its own database and from remote agents in the same dashboard.
