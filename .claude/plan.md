# Plan: Tharga.MongoDB.Monitor.Server package

## Steps

### 1. Project skeleton ✓
- [x] Created `Tharga.MongoDB.Monitor.Server` project (net8.0;net9.0;net10.0)
- [x] References: `Tharga.MongoDB.Monitor.Client` (project ref), `Tharga.Communication` v0.0.3 (NuGet)
- [x] Added to solution, test project references it
- [x] Solution builds

### 2. IngestCall on IDatabaseMonitor ✓
- [x] Added `void IngestCall(CallDto call)` to `IDatabaseMonitor`
- [x] `DatabaseMonitor.IngestCall` converts `CallDto` → `CallInfo` via `FromCallDto`, feeds into `ICallLibrary`
- [x] No-op in `DatabaseNullMonitor`
- [x] `ICallLibrary.IngestCall(CallInfo)` inserts into recent calls, slow calls, and call counts
- [x] 3 tests: appears in last calls, appears in slow calls, increments call count

### 3. MonitorCallHandler — receives remote calls ✓
- [x] Created `MonitorCallHandler : PostMessageHandlerBase<MonitorCallMessage>`
- [x] Handler calls `IDatabaseMonitor.IngestCall(call.Call)`
- [x] Test: verifies IngestCall called with correct data

### 4. Client state service and repository defaults ✓
- [x] `MonitorClientConnectionInfo` record implementing `IClientConnectionInfo`
- [x] `MonitorClientStateService` extending `ClientStateServiceBase<MonitorClientConnectionInfo>`
- [x] `MonitorClientRepository` extending `MemoryClientRepository<MonitorClientConnectionInfo>`
- All simple defaults — consumers can override

### 5. Registration ✓
- [x] `AddMongoDbMonitorServer()` on `WebApplicationBuilder` — registers Communication server with monitor defaults
- [x] `UseMongoDbMonitorServer()` on `WebApplication` — maps hub endpoint
- [x] `MonitorCallHandler` discovered automatically by Tharga.Communication handler scan
- [x] Added `FrameworkReference` to `Microsoft.AspNetCore.App` in csproj

### 6. Tests ✓
- [x] IngestCallTests (3): ingested call in last calls, slow calls, call counts
- [x] MonitorCallHandlerTests (1): handler invokes IngestCall with correct data
- [x] MonitorServerPipelineTests (2): full pipeline end-to-end, multiple sources
- All 234 tests pass

### 7. Pipeline — pack new NuGet packages ✓
- [x] Added pack steps for `Tharga.MongoDB.Monitor.Client` and `Tharga.MongoDB.Monitor.Server` to `azure-pipelines.yml`

### 8. Final validation
- [ ] Full test suite passes
- [ ] Solution builds in Release
- [ ] Commit all changes
- [ ] Summarize and ask user to test

## Notes
- Message types come from the Client package (no duplication)
- `IngestCall` feeds directly into `ICallLibrary`, so Blazor components see remote data without changes
- Subscription-based sending (only forward when someone is watching) is deferred until Tharga.Communication adds that capability
