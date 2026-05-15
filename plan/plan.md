# Plan: Monitor source-column visibility fix

Feature scope: see [feature.md](feature.md). Branch: `feature/monitor-multi-client-ui`.

## Steps

### Phase 1 — Tag local collections in `_collectionSources` ✅

- [x] **1.1** First pass used read-time synthesis in `GetCollectionSources`. Reverted on user feedback — multi-actor topology (any source can touch any collection) and "touched ≡ source recorded" semantics demand symmetric write-time tagging, mirroring how `IngestCollectionInfo` tags remote touches.
- [x] **1.2** Added two helpers in `DatabaseMonitor`:
  - `TagLocalSource(key)` — sets `_collectionSources[key][localSource] = true`. Idempotent.
  - `RaiseLocalCollectionInfoChanged(info)` — tags + raises `CollectionInfoChangedEvent`. Used wherever local code fires that event.
  - `TagLocalSource` is called explicitly at the top of the `CollectionAccessEvent` handler so the tag survives early-return paths (e.g. when the cache update short-circuits).
  - All five local `CollectionInfoChangedEvent?.Invoke(this, new CollectionInfoChangedEventArgs(updated))` invocations replaced with `RaiseLocalCollectionInfoChanged(updated)`. The remote one in `IngestCollectionInfo` is unchanged (it already tags the remote source explicitly).
  - `GetCollectionSources` reverted to its original one-liner.
- [x] **1.3** Three unit tests in `RemoteActionDelegationTests` rewritten to drive a real `CollectionAccessEvent` via Moq's `Raise`:
  - `GetCollectionSources_IncludesLocalSource_AfterCollectionAccessEvent`
  - `GetCollectionSources_OmitsLocalSource_WhenOnlyRemoteHasReported`
  - `GetCollectionSources_IncludesBothSources_WhenLocalAndRemoteBothTouch`
- [x] **1.4** Targeted suite (`RemoteActionDelegationTests`, `IngestCollectionInfoTests`, `MonitorSourceTests`, `MonitorServerPipelineTests`, `IngestCallTests`, `MonitorCallHandlerTests`, `MonitorForwarderTests`) all green — 38/38.

### Phase 2 — Refresh CallView source list on incoming calls ✅

- [x] **2.1** `CallView.OnCallChanged` now always calls `UpdateFilterOptions()` so the filter set refreshes regardless of tab; `ReloadDataAsync()` still only fires on Ongoing (preserves existing behavior).
- [x] **2.2** Moved `_showSource = _sources.Length > 1` from `ReloadDataAsync` into `UpdateFilterOptions` so column visibility tracks the filter set, not the displayed-tab subset. `_showConfiguration` and `_showDatabase` stay in `ReloadDataAsync` (they derive from `_all`, not the filter union).

### Phase 3 — Test + smoke

- [ ] **3.1** Full `dotnet test -c Release` pass. Pre-existing Lockable test flakiness is unrelated (verified by stash + run on bare master — same failures).
- [ ] **3.2** Smoke against Eplicta: run Aggregator + at least one Agent locally, confirm Source column visible on both tabs, filter narrows rows, new agent calls appear in the source list without page reload.
- [ ] **3.3** While smoking, quickly verify issue #99 Ask #4 (remote-only collection drill-down) — open a remote collection's dialog, confirm it renders rather than refusing. Note result in the PR description.

### Phase 4 — Close out

- [ ] **4.1** Commit, push branch.
- [ ] **4.2** User validation from origin.
- [ ] **4.3** On approval: archive `plan/feature.md` to `done/monitor-source-column-fix.md`, file the per-agent-dialog as `planned/02-monitor-agent-detail-dialog.md`, `git rm -r plan`, final commit `fix: monitor-source-column-fix complete`, push, open PR referencing #99 (not closing).

## Last session

2026-05-15 — Phase 1 and Phase 2 both shipped:

- **Phase 1**: `DatabaseMonitor.GetCollectionSources` now includes the local `SourceName` for locally-cached keys. Three new unit tests added to `RemoteActionDelegationTests`. Targeted suite green (28/28).
- **Phase 2**: `CallView.OnCallChanged` always refreshes filter options; `_showSource` moved to `UpdateFilterOptions` so column visibility reacts to filter-set changes, not just current-tab grid reloads. Blazor builds clean, 0 warnings.

Pre-existing Lockable test failures observed in the full suite are unrelated (verified by running them on bare master — same failures, same count).

Next: Phase 3.1 (full test pass for completeness) and Phase 3.2 (Eplicta smoke). Then Phase 4 close-out.
