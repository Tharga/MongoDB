# Plan: Tharga.MongoDB.Monitor.Client package

## Steps

### 1. Project skeleton ✓
- [x] Create `Tharga.MongoDB.Monitor.Client` project (class library, net8.0;net9.0;net10.0)
- [x] Add references: `Tharga.MongoDB` (project ref), `Tharga.Communication` v0.0.3 (NuGet)
- [x] Add project to solution
- [x] Verify solution builds
- Note: `InternalsVisibleTo` deferred until tests need it

### 2. Hub contract — message types ✓
- [x] Create `MonitorCallMessage` record wrapping `CallDto`
- [x] Create `MonitorCollectionInfoMessage` for collection info changes
- [x] Types live in Client project (Server will reference Client)

### 3. MonitorForwarder — core forwarding service ✓
- [x] Create `MonitorForwarder` hosted service subscribing to `CallStartEvent` / `CallEndEvent`
- [x] Captures start events in `ConcurrentDictionary`, merges with end event to build `CallDto`
- [x] On `CallEndEvent` with `Final == true`, posts via `IClientCommunication.PostAsync<MonitorCallMessage>`
- [x] Skips non-final events; skips send when not connected
- [x] Fire-and-forget — all exceptions caught and logged at Debug level

### 4. Configuration — `SendTo` option ✓
- [x] Added `string SendTo` property to `MonitorOptions` (the server URL)
- [x] Created `AddMongoDbMonitorClient` extension on `IHostApplicationBuilder`
- [x] When `sendTo` is null/empty: returns immediately — zero overhead
- [x] When set: registers Tharga.Communication client with that URL + `MonitorForwarder` as hosted service
- Note: consumer usage is `builder.AddMongoDbMonitorClient(sendTo: "https://server/hub")` — explicit call, not wired into `AddMongoDB` automatically (to avoid Tharga.MongoDB depending on Tharga.Communication)

### ~~5. Registration wiring~~ (merged into step 4)

### 6. Tests ✓
- [x] Test: `FinalCallEnd_ForwardsCallDto` — verifies PostAsync called with correct CallDto fields
- [x] Test: `NonFinalCallEnd_DoesNotForward` — intermediate updates are skipped
- [x] Test: `NotConnected_DoesNotSend` — no PostAsync when client is disconnected
- [x] Test: `ForwardingFailure_DoesNotThrow` — exception in PostAsync is swallowed
- [x] Test: `CallEndWithSteps_ForwardsSteps` — step data is correctly mapped
- [x] Test: `CallEndWithException_ForwardsExceptionMessage` — exception message is forwarded
- All 228 tests pass (6 new)

### 7. Feature request to Tharga.Communication ✓
- [x] Created `c:\dev\tharga\Toolkit\Communication\.claude\requests.md`
- [x] Requested subscription-based messaging: server signals watching/not-watching → client starts/stops forwarding
- [x] Referenced Tharga.MongoDB.Monitor.Client as motivation

### 8. Final validation ✓
- [x] Solution builds in Release
- [x] 228 tests pass, 8 skipped (pre-existing intermittent lockable test flakes unrelated to this feature)
- [ ] Commit all changes
- [ ] Summarize and ask user to test

## Notes
- DTOs live in Client package to avoid a standalone Shared package that no one should reference alone
- Server package (Feature #12) will reference Client package to get the message types
- Subscription-based sending (only forward when someone is watching) is deferred until Tharga.Communication adds that capability
