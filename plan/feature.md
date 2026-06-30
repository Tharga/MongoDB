# Feature: Show Monitor client & server library versions in the Blazor UI

## Goal
Surface, in the Blazor monitoring dashboard, the assembly version of the Monitor **client** library (`Tharga.MongoDB.Monitor.Client`) per connected agent and the Monitor **server** library (`Tharga.MongoDB.Monitor.Server`) for the dashboard host. The existing "Version" column shows the agent host-app version (from Tharga.Communication) — distinct, and kept.

## Scope
- Core: public `GetLibraryVersion(this Assembly)` helper; `LibraryVersion` on `MonitorClientStatus`; public `IMonitorServerInfo`.
- Client: add `LibraryVersion` to `MonitorClientStatusMessage`, set it in `MonitorForwarder.SendClientStatusAsync`.
- Server: map `LibraryVersion` in the status handler; register `IMonitorServerInfo` in `AddMongoDbMonitorServer`.
- Blazor: "Library" column + dialog field (client version); server-version caption.

## Acceptance criteria
- Clients grid shows the Monitor.Client library version per agent; the client detail dialog shows it.
- The dashboard shows the Monitor.Server library version.
- Build + full test suite green (excluding ~6 environmental replica-set tests).

## Done condition
Acceptance met, tests pass, minor version bumped, docs updated, PR opened to `master`.
