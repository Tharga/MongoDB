# Plan: Connected clients view in Blazor dashboard

## Steps

### 1. MonitorClientDto and IDatabaseMonitor additions ✓
- [x] `MonitorClientDto` record with ConnectionId, Machine, Type, Version, IsConnected, ConnectTime, DisconnectTime
- [x] `GetMonitorClients()`, `IngestClientConnected()`, `IngestClientDisconnected()`, `MonitorClientsChanged` event on `IDatabaseMonitor`
- [x] `DatabaseMonitor` — in-memory `ConcurrentDictionary`, fires event on change
- [x] `DatabaseNullMonitor` — no-op implementations
- [x] Fixed `IngestOnlyMonitor` test helper

### 2. Ingest client state from Monitor.Server ✓
- [x] `IngestClientConnected` / `IngestClientDisconnected` already on `IDatabaseMonitor` (step 1)
- [x] `MonitorClientBridge` hosted service subscribes to `ConnectionChangedEvent` / `DisconnectedEvent`
- [x] Converts `MonitorClientConnectionInfo` to `MonitorClientDto` and calls ingest methods
- [x] Registered as hosted service in `AddMongoDbMonitorServer`

### 3. ClientsView.razor component ✓
- [x] RadzenDataGrid with columns: Status (badge), Machine, Application, Version, Connected, Disconnected
- [x] Green "Connected" / red "Disconnected" badges
- [x] Disconnected rows rendered with 50% opacity
- [x] Subscribes to `MonitorClientsChanged` for auto-refresh
- [x] Empty state: "No monitoring agents connected."

### 4. Wire into sample dashboard ✓
- [x] Added "Clients" tab after "Calls" in Database.razor
- [x] Always shown — displays empty state when no agents

### 5. Final validation ✓
- [x] Solution builds in Release
- [x] 234 tests pass
- [ ] Commit and summarize
