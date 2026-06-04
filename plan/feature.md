# Feature: Failed-index recheck strategy

Closes [#110](https://github.com/Tharga/MongoDB/issues/110) on merge. Supersedes the original "swap order + clear on success" framing — the underlying retry-on-touch strategy is being removed entirely, not optimised.

## Goal

Stop checking index health on every data write. Replace the current "every Update/Delete arms a retry" pattern with a once-per-session assure plus three explicit recovery paths: an optional eager startup pass, an optional periodic sweep that wakes only when a failure exists, and the existing operator-driven retry buttons. Successful retries should clear stale failed-index entries so consumer UIs (e.g. Florida's admin) reflect reality.

## Background

`DiskRepositoryCollectionBase.OperationIndexManagement` currently calls `ArmRecheckInvalidIndex()` on `Operation.Update` and `Operation.Delete`. That arming flips `IndexAssured` back to `false` so the *next* index-managed operation will re-run `AssureIndex`. The intent was self-healing recovery: fix the duplicate data, do any write, the index quietly comes back.

In practice this strategy:

- **Re-runs the assure check on essentially every data write** after a failure has been recorded — even when the underlying data conflict is unchanged.
- **Takes two touches to actually retry** (#110's original observation): one touch arms, the next touch fires.
- **Leaves stale entries in `FailedIndices`** even after a successful rebuild, because the in-memory state was append-only.

The cumulative effect: index-assurance overhead has crept into the hot data-write path with marginal recovery benefit.

## Scope

### 1. Remove retry-on-touch

Delete the `ArmRecheckInvalidIndex()` arming from `OperationIndexManagement(Update)` and `OperationIndexManagement(Delete)`. Once-per-session assurance (driven by `ShouldInitiateIndex` / `IndexAssured` on first access) becomes the *only* implicit retry trigger.

The `RecheckInitiateIndex` method on `IInitiationLibrary` stays — it's used by the explicit `RestoreIndexAsync` paths to re-arm assurance before retrying. Just no longer invoked from the data-write side.

### 2. `DatabaseOptions.AssureIndexAtStartup` (eager mode)

New `bool` option, default `false`. When `true`, `UseMongoDB` iterates registered collections at startup and triggers `AssureIndex` for each. Failures are logged and recorded in `_initiationLibrary` the same way as the lazy first-access path — no startup-throwing, no fail-fast (per design decision: log and continue).

Useful for web hosts that want index-readiness reflected before traffic arrives.

### 3. `DatabaseOptions.FailedIndexRecheckInterval` (background sweep)

New `TimeSpan?` option, default `TimeSpan.FromHours(1)`. When non-null, the package registers a small `BackgroundService` that loops with `Task.Delay(interval)` between ticks. Each tick:

- Queries the new `IDatabaseMonitor.GetCollectionsWithFailedIndices()` helper.
- If empty, returns immediately (no work, no log noise — the "dormant when healthy" property).
- Otherwise, for each collection with failed indexes, calls the equivalent of `RestoreIndexAsync` to retry the failed indexes.

The implicit "start when failed, stop when solved" behaviour falls out of the empty-skip — the `Task.Delay` between ticks is a sleeping task (no CPU), so a healthy app pays nothing in steady state.

Set to `null` to disable entirely (e.g. for consumers with their own auditor like Florida).

### 4. `IInitiationLibrary.ClearFailedIndex`

New method clearing a single `(Operation, IndexName)` entry for a specific collection. Wired into the per-index success paths inside `UpdateIndicesByNameAsync` / `BySchemaAsync` / `ByDropCreateAsync` so a successful `CreateOneAsync` (or `DropOneAsync` for the drop-failure side) brings the in-memory `FailedIndices` set back in sync with reality — regardless of trigger path (startup, sweep, `TouchAsync`, `RestoreIndexAsync`, the Blazor "Restore Index" button).

### 5. `TouchAsync` triggers a fresh `AssureIndex` pass

`IDatabaseMonitor.TouchAsync(CollectionInfo)` already refreshes stats + index metadata. Extend (or document, if already so) to also trigger `RecheckInitiateIndex` + a fresh assure pass for the touched collection. Gives consumers a programmatic opportunistic recovery hook on a single collection without needing to know about `RestoreIndexAsync`.

### 6. New helper: `GetCollectionsWithFailedIndices`

Added on `IDatabaseMonitor` (since the sweep + any consumer UI will benefit). Internally scans `_initiationLibrary` state for collections with non-empty `FailedIndices` and returns them as `CollectionInfo` (or `CollectionFingerprint`) entries — TBD at impl time based on which fits the call sites best.

## Out of scope

- **Eplicta migration documentation.** EP-4156 solved this manually on their side; no migration text needed in the PR.
- **Fail-fast startup mode.** `AssureIndexAtStartup = true` only logs failures and continues. A consumer who wants the app to refuse to start on index failure can wrap their own check around `RestoreAllIndicesAsync`. Reconsider if a real consumer asks.
- **Per-collection opt-out**. Index assurance strategy is host-level concern; no per-collection `AssureIndexAtStartup` / `FailedIndexRecheckInterval` overrides.
- **`IndexFailure` API surface changes.** The public `GetFailedIndices()` from #106 stays as-is; only the IN-PROCESS lifecycle changes here.

## Acceptance criteria

- `Operation.Update` and `Operation.Delete` no longer trigger an index re-check — verified by a test that exercises the `OperationIndexManagement` path after a failure was recorded.
- `DatabaseOptions.AssureIndexAtStartup = true` triggers `AssureIndex` for every registered collection during `UseMongoDB` startup; failures are logged + recorded but don't throw.
- `DatabaseOptions.FailedIndexRecheckInterval = TimeSpan.FromSeconds(N)` causes a `BackgroundService` to tick on that cadence; in the steady state (no failed indexes) each tick is a no-op; after a failure is recorded the next tick retries the affected indexes.
- A successful retry (from any path — sweep, startup, `TouchAsync`, `RestoreIndexAsync`) clears the corresponding `(Operation, Name)` entry from `_initiationLibrary.FailedIndices` for that collection. Other entries are untouched.
- `TouchAsync` triggers a fresh assure pass and clears failed entries that successfully retry.
- Existing tests stay green (modulo the pre-existing Lockable transaction-test cohort).
- New tests cover each numbered behaviour.

## Done condition

- Acceptance criteria met.
- `MongoDB.md` backlog: no entry to remove (this feature wasn't there). Plan archived to `done/`; `planned/README.md` updated.
- PR opens with `closes #110` in the description.

## Effort

Medium. ~5 production-code touchpoints (remove arming, add option × 2, add helper + sweep service, wire `ClearFailedIndex`, extend `TouchAsync`), plus tests covering each.

## NuGet

Current. No bumps needed.
