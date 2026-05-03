# Plan: Generalised document lock with commit-time Update/Delete decision

## Steps

### Step 1: Audit existing single-doc lock paths — DONE
- [x] Read `LockableRepositoryCollectionBase<TEntity, TKey>` end-to-end. The relevant existing pieces:
  - **`CommitMode` enum already exists, public, with `Update` / `Delete`.** Reuse it; do NOT add `LockCommitMode`.
  - **`CreateLockAsync(filter, timeout, actor, commitMode, completeAction, failIfLocked)`** is already the shared helper used by all 6 `Pick*` overloads + `WaitForLock`. It takes `CommitMode` at lock time, builds the per-mode `releaseAction` closure (lines 649–662), and constructs `EntityScope<TEntity, TKey>`.
  - **`ReleaseAsync(...)` is already mode-agnostic** — it takes a release-action delegate. The mode-specific work happens in `PrepareCommitForUpdateAsync` and `PerformCommitForDeleteAsync` (lines 726 and 746).
  - `LockEvent`, `CallbackResult<T>`, `LockEventArgs`, `LockAction`, `BuildLockInfo`, `LockAlreadyReleasedException`, `LockExpiredException`, `UnlockDifferentEntityException`, `CommitException`, `LockException`, `LockErrorException`, `UnknownException`, `ErrorInfo` — all in place; reuse, don't redefine.
- [x] Read `EntityScope<TEntity, TKey>` (in pre-plan audit). Existing release-action signature `Func<T, bool, Exception, Task>` — `commit` flag determines update vs abandon, but the mode is already baked into the closure at construction.
- [x] Build solution — clean (15 warnings, under 50 budget)

### Refactor strategy (informs Steps 2–4)
- **Don't create a new helper from scratch.** Split the existing `CreateLockAsync` into two methods:
  - **New private `AcquireLockAsync(filter, timeout, actor, failIfLocked)`** — pure acquisition: atomic find-and-update + the failure-mode resolution (locked / expired / not-found). Returns `(TEntity Locked, Lock EntityLock, ErrorInfo errorInfo, bool ShouldWait)`. No `CommitMode`, no scope construction.
  - **`CreateLockAsync` becomes a thin wrapper** for the legacy `Pick*` paths: calls `AcquireLockAsync`, builds the mode-specific `releaseAction`, constructs `EntityScope`. Behavior unchanged.
- **New `LockAsync`** calls `AcquireLockAsync` directly and constructs a `LockScope` whose release action dispatches on the `CommitMode` passed at commit time (chooses between `PrepareCommitForUpdateAsync` and `PerformCommitForDeleteAsync` per call).

### Step 2: Single-doc public API surface — DONE
- [x] **Reused existing public `CommitMode` enum** (`Update`/`Delete`)
- [x] Added `LockScope<TEntity, TKey>` (and the `ObjectId`-defaulted convenience subclass `LockScope<T>`) under `Tharga.MongoDB.Lockable`. Mirrors `EntityScope` shape: `Entity`, `CommitAsync(CommitMode, TEntity = null)`, `AbandonAsync`, `SetErrorStateAsync`, `IAsyncDisposable` + `IDisposable`. Internal release-action delegate `Func<TEntity, CommitMode?, Exception, Task>` — null mode = abandon, exception != null = error state.
- [x] Added 3 `LockAsync` overloads on `ILockableRepositoryCollection<TEntity, TKey>` (id / filter / predicate)
- [x] Stubbed the implementations on `LockableRepositoryCollectionBase<TEntity, TKey>` with `NotImplementedException`
- [x] No test mocks to update (only the interface and a sample subclass implement it; the latter inherits the stubs)
- [x] Build clean (9 warnings on Tharga.MongoDB project, all pre-existing); 128 Lockable tests pass — no regressions

### Step 3: Split `CreateLockAsync` into `AcquireLockAsync` + scope-building wrapper — DONE
- [x] Extracted private `AcquireLockAsync(filter, timeout, actor, failIfLocked)` returning `(TEntity Entity, Lock EntityLock, ErrorInfo ErrorInfo, bool ShouldWait)` — pure acquisition, no scope construction. All error messages preserved verbatim.
- [x] Refactored `CreateLockAsync` to call `AcquireLockAsync`, build the per-mode release action (`PrepareCommitForUpdateAsync` / `PerformCommitForDeleteAsync`), construct `EntityScope`, and signal `_releaseEvent`. Legacy callers (`Pick*`, `WaitForLock`) untouched.
- [x] Full Lockable test suite: 128/128 pass (regression gate green).
- [x] Full repo test suite: 320 passed / 8 skipped / 0 failed.
- [x] Build clean (9 warnings on Tharga.MongoDB project, all pre-existing).

### Step 4: Implement single-doc `LockAsync` + `LockScope` — DONE
- [x] All 3 `LockAsync` overloads now delegate to `AcquireLockAsync` (id uses failIfLocked=true, filter/predicate use false — matches existing `Pick*` semantics) and return a `LockScope` built by a new private `BuildLockScope` helper
- [x] `BuildLockScope` constructs the `Func<TEntity, CommitMode?, Exception, Task>` release-action lambda that dispatches on the mode passed at commit time:
  - `CommitMode.Update` → `ReleaseAsync(..., commit: true, ..., PrepareCommitForUpdateAsync)`
  - `CommitMode.Delete` → `ReleaseAsync(..., commit: true, ..., PerformCommitForDeleteAsync)`
  - `null` mode (AbandonAsync / SetErrorStateAsync) → `ReleaseAsync(..., commit: false, ...)` with no per-mode dispatcher
  - `_releaseEvent.Set()` is called once at lock-acquire time, mirroring the legacy `CreateLockAsync` behavior
- [x] Added a small overload `ThrowException(ErrorInfo)` so the new entry points can pass an `ErrorInfo` directly without constructing a fake tuple. Existing tuple overload now forwards to it.
- [x] Build clean
- [x] Lockable regression: 128/128 still pass

### Step 5: Multi-doc public API surface — DONE
- [x] Added `DocumentLeaseCommitSummary<TKey>` and `DocumentLeaseFailure<TKey>` records under `Tharga.MongoDB.Lockable` (generic on TKey for typed Id without casts; `IntendedDecision` is `CommitMode?` where null = release)
- [x] Added `DocumentLease<T>` (defaulted to ObjectId) and `DocumentLease<T, TKey>` class skeleton with `Documents`, `MarkForUpdate(T)`, `MarkForUpdate(TKey, T)`, `MarkForDelete(TKey)`, `MarkRelease(TKey)`, `CommitAsync(CancellationToken)`, `IAsyncDisposable.DisposeAsync`, `IDisposable.Dispose` — all members `virtual` and currently throw `NotImplementedException`. Internal parameterless constructor.
- [x] Added 3 `LockManyAsync` overloads on `ILockableRepositoryCollection<TEntity, TKey>` (id list / filter / predicate). Signature uses `CancellationToken` (no `completeAction` for the multi-doc case — keeps it simple; can add overloads if needed)
- [x] Stubbed implementations on `LockableRepositoryCollectionBase<TEntity, TKey>` with `NotImplementedException`
- [x] Build clean; no test mock ripples (no mocks of `ILockableRepositoryCollection` in the codebase)
- [x] Lockable regression: 128/128 still pass

### Step 6: Implement multi-doc `LockManyAsync` + `DocumentLease` — DONE
- [x] `LockManyAsync(IEnumerable<TKey> ids, ...)` deduplicates and sorts ids via `Comparer<TKey>.Default`, then calls `AcquireLockAsync` per id (failIfLocked=true). On any failure, releases already-acquired locks via the abandon path and rethrows. Empty id list returns an empty lease.
- [x] Filter / predicate overloads resolve to an id list via `GetAsync(filter).Select(e => e.Id).ToArrayAsync(ct)` and delegate. The predicate overload converts to `Builders<TEntity>.Filter.Where(predicate)` first.
- [x] `DocumentLease<T, TKey>` holds `IReadOnlyList<DocumentLeaseEntry<T, TKey>>` (internal record carrying entity + per-doc release-action delegate) and an id-keyed dictionary for fast lookup. Mark methods stage into `Dictionary<TKey, (CommitMode? Mode, T Updated)>` plus a parallel `List<TKey> _markOrder` to preserve insertion order. Throws `ArgumentException` when marking an unknown id; re-marking replaces the previous decision (last-write-wins, position preserved).
- [x] `CommitAsync` iterates `_markOrder`, dispatches each to the per-entry release-action with the staged mode, increments the right counter (`Updated`/`Deleted`/`ReleasedUnchanged`) or appends to `Failures`. Unmarked entries are released-unchanged after the marked pass. Returns `DocumentLeaseCommitSummary<TKey>`. Honors cancellation between operations.
- [x] `DisposeAsync` releases all entries if neither committed nor disposed (best-effort, swallows release errors). `Dispose` schedules `DisposeAsync` — same shape as `EntityScope`.
- [x] Build clean (9 pre-existing warnings); 128/128 Lockable tests still pass.

### Step 7: Tests

#### Single-doc (covers Steps 3 + 4)
- [ ] `LockAsync(id) + CommitAsync(Update, updated)` → document updated; `Lock` cleared; `LockEvent` fires
- [ ] `LockAsync(id) + CommitAsync(Update)` with no updatedEntity → commits the original entity unchanged; lock cleared
- [ ] `LockAsync(id) + CommitAsync(Delete)` → document deleted
- [ ] `LockAsync(id) + AbandonAsync` → no change, lock released
- [ ] `LockAsync(id)` then dispose without commit → abandon (mirrors existing `EntityScope` dispose)
- [ ] `LockAsync(id)` against an already-locked doc → respects timeout, throws the same exception type as `PickFor*` against locked
- [ ] `LockAsync(id) + SetErrorStateAsync(ex)` → records exception state on the lock
- [ ] `CommitAsync` after release → `LockAlreadyReleasedException`
- [ ] Filter / predicate overloads happy-path
- [ ] **Regression: existing `Lockable` test suite passes without edits**

#### Multi-doc (covers Steps 5 + 6)
- [ ] Mixed-decision happy path: `LockManyAsync([id1, id2, id3])`, mark one update + one delete + one release, commit, verify each doc's final state and the summary counts
- [ ] Lock-acquire failure: arrange a doc that's already locked elsewhere; assert `LockManyAsync` throws and partial locks were released (verified by re-locking succeeding for the other ids)
- [ ] Commit failure: arrange one decision to fail; assert `Failures` lists it but other decisions still committed
- [ ] Disposal without commit: `LockManyAsync` 2 docs, dispose without `CommitAsync`; assert both can be re-locked immediately
- [ ] `LockManyAsync` with empty id list → returns an empty lease (no acquisitions; commit returns zeros; disposal is a no-op)
- [ ] Mark methods reject unknown ids with `ArgumentException`
- [ ] `LockEvent` fires once per acquisition (N events for N ids)

### Step 8: Build verification on all targets
- [ ] Build on net8 / net9 / net10 — clean, warnings under 50 budget
- [ ] McpProviderTests + full suite — green on net10

### Step 9: README update
- [ ] Add "Unified lock with commit-time decision" subsection to the Lockable docs
  - Single-doc example: `await using var scope = await coll.LockAsync(id); ... await scope.CommitAsync(LockCommitMode.Delete);`
  - Multi-doc example: `await using var lease = await coll.LockManyAsync([id1, id2, id3]); lease.MarkForDelete(...); lease.MarkForUpdate(...); var summary = await lease.CommitAsync();`
- [ ] Note that `PickForUpdateAsync` / `PickForDeleteAsync` are still first-class for the "I know the outcome up front" case
- [ ] Note partial-failure semantics for the multi-doc lease and the planned transactional follow-up

### Step 10: Milestone commit
- [ ] Commit message: `feat: add Lock + LockMany with commit-time mode (closes planned #30 + #31)`

### Step 11: Closure (per shared-instructions § "Closing a feature")
- [ ] Archive `plan/feature.md` → `$DOC_ROOT/Tharga/plans/Toolkit/MongoDB/done/generalized-document-lock.md`
- [ ] Delete `$DOC_ROOT/Tharga/plans/Toolkit/MongoDB/planned/30-generalized-document-lock.md` (now superseded by the archive in `done/`)
- [ ] Delete `$DOC_ROOT/Tharga/plans/Toolkit/MongoDB/planned/31-refactor-lock-for-update-delete.md` (closed by this feature; refactor shipped as Step 3)
- [ ] Update `$DOC_ROOT/Tharga/plans/Toolkit/MongoDB/planned/README.md`: drop #30 and #31 rows; add bullet to "Done" recent list
- [ ] Add a new follow-up entry to the Plan dir or `Requests.md`: *"Add transactional commit overload to `DocumentLease` once #29 lands — `CommitAsync(IClientSessionHandle session)`"*
- [ ] Delete `plan/` directory from the repo
- [ ] Final commit: `feat: generalized-document-lock complete`

### Step 12: Push + PR
- [ ] User pushes `feature/generalized-document-lock` to origin
- [ ] Claude opens PR to `Tharga/master`
- [ ] After merge — delete the feature branch (local + remote)

## Notes

### Why the shared `AcquireLockAsync` matters
Three entry points (`LockAsync`, `LockManyAsync`, legacy `PickFor*`) all need the same atomic find-and-update against `LockedFilter` / `LockedOrExceptionFilter`, plus the same `LockEvent` firing, timeout handling, and actor recording. Pulling that into one helper:
- Keeps the new code thin (Steps 4 and 6 are mostly composition)
- Means the regression gate (Step 3) protects the legacy paths from inadvertent behavior changes
- Means future changes to lock semantics happen in one place

### Why a separate `LockScope` instead of extending `EntityScope`
Existing `EntityScope.CommitAsync(T updated = null)` has commit semantics baked in at construction (update-flavor for PickForUpdate, delete-flavor for PickForDelete). Adding `CommitAsync(LockCommitMode, T)` to it would create dual-mode behavior that depends on construction — confusing. New type keeps the primitive clean and leaves the existing tests untouched.

### Deterministic lock ordering (multi-doc)
Sort by id before acquiring. Two leases targeting overlapping sets always lock in the same order — no AB / BA deadlock. The cost is just a sort over the input id list before the first acquisition.

### Closes which planned items
- **planned/30** — substance of the feature, minus the deferred transactional-commit overload
- **planned/31** — done in Step 3 (refactor of `PickFor*` over the new shared helper)

## Last session
Plan rewritten with expanded scope: now covers both **single-doc `LockAsync`** (Phase 1) and **multi-doc `LockManyAsync` + `DocumentLease`** (Phase 2) in one feature. Closes planned/30 and planned/31. Branch `feature/generalized-document-lock` already on master tip (`c384c72`). Awaiting confirmation before Step 1.
