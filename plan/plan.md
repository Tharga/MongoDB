# Plan: Per-agent detail dialog in `ClientsView`

Feature scope: see [feature.md](feature.md). Branch: `feature/monitor-agent-detail-dialog`. Closes [#99](https://github.com/Tharga/MongoDB/issues/99) on merge.

## Steps

### Phase 1 — Data accessor on `IDatabaseMonitor` ✅

- [x] **1.1** New public record `MonitorClientDetail` (`Tharga.MongoDB/MonitorClientDetail.cs`) — wraps `MonitorClientDto` identity, `IReadOnlyCollection<string> CollectionKeys`, `IReadOnlyCollection<CallInfo> RecentCalls`, and `ConnectionPoolStateDto QueueState`. Used `ConnectionPoolStateDto` (already public) for queue state instead of inventing a new type.
- [x] **1.2** `GetMonitorClientDetail(string sourceName, int recentCallLimit = 20)` added on `IDatabaseMonitor`; implementation in `DatabaseMonitor`. Returns null when source is unknown or empty. Stub added to `DatabaseNullMonitor` and the test-side `IngestOnlyMonitor` so the interface stays buildable everywhere.
- [x] **1.3** Five tests in `RemoteActionDelegationTests`:
  - `GetMonitorClientDetail_ReturnsNull_ForUnknownSource`
  - `GetMonitorClientDetail_ReturnsOnlyCollectionsTaggedToThisSource`
  - `GetMonitorClientDetail_RecentCalls_AreFilteredBySourceName_AndCapped`
  - `GetMonitorClientDetail_ReturnsQueueState_WhenAgentHasReportedOne`
  - `GetMonitorClientDetail_GracefullyEmptyForFreshlyConnectedAgent`
- [x] **1.4** Targeted monitor sweep — 55/55 green (includes the new 5 + the existing source-tagging, fingerprint, ingest, server-pipeline, forwarder, source-name suites).

### Phase 2 — Dialog UI in `Tharga.MongoDB.Blazor` ✅

- [x] **2.1** New `MonitorClientDialog.razor` with `[Parameter] MonitorClientDto Client` + `[Parameter] int RecentCallLimit = 20`. Four sections (identity, collections, recent calls, queue snapshot) plus a connection-state badge in the header. Empty-state placeholders (`"No <thing> reported yet."`) on every section.
- [x] **2.2** Refresh button calls `RefreshAsync` which re-pulls via `DatabaseMonitor.GetMonitorClientDetail`. No subscription lifecycle — host's existing timer tick is enough per the spec.
- [x] **2.3** `BadgeStyle.Success "Connected"` / `BadgeStyle.Danger "Disconnected"` mirrors the existing `ClientsView` row style.
- [x] **2.4** `ClientsView.razor` injects `DialogService`, adds `RowClick` opening the dialog (800px, resizable + draggable) titled by SourceName, and `RowRender` sets `cursor: pointer` so rows look clickable.
- [x] **2.5** Matches `CollectionDialog`/`CallDialog` patterns — `RadzenStack`/`RadzenRow`/`RadzenColumn` layout, `RadzenText TextStyle.Caption` labels, `RadzenDataGrid Density.Compact` for the calls grid, `Tharga.Blazor.DateTimeView` / `TimeSpanView` for timestamps. Blazor builds clean (0 warnings).

### Phase 3 — NuGet bump ✅

- [x] **3.1** `Tharga.Blazor` 2.1.4 → 2.1.6 bumped in `Tharga.MongoDB.Blazor.csproj`. Will be committed separately from the API+UI commit so it's easy to revert.
- [x] **3.2** Restore + Blazor build clean. No breaking-change surprises.

### Phase 4 — Tests + smoke

- [x] **4.1** Full `dotnet test -c Release`: 430 passed, 6 Lockable failures from the known pre-existing flaky cohort (5 transaction tests + 1 timing-dependent DeleteMany).
- [ ] **4.2** Smoke against Eplicta (user-driven from origin once branch is pushed).

### Phase 5 — Close-out

- [ ] **5.1** Commit at each logical milestone: API surface + tests, dialog UI, NuGet bump.
- [ ] **5.2** Push branch.
- [ ] **5.3** User validation against Eplicta.
- [ ] **5.4** On confirmation: archive `plan/feature.md` to `done/monitor-agent-detail-dialog.md`, file the spec under `planned/README.md` Done section, `git rm -r plan`, final commit `feat: monitor-agent-detail-dialog complete`, push, open PR with `closes #99` in description.

## Last session

Plan written. No code yet. Awaiting user confirmation before starting Phase 1.

## Phase 1 results

_To be filled in._
