# Plan: Lockable delayed commit

Feature scope: see [feature.md](feature.md). Branch: `feature/lockable-delayed-commit`.

## Steps

### Phase 1 — Options surface ✅

- [x] **1.1** Added `bool AllowDelayedCommit { get; set; } = true;` to `DatabaseOptions` at the top level. XML doc explains the global default + the per-collection override pointer.
- [x] **1.2** Binding from `appsettings.json` follows the standard pattern — no extra wiring needed.

### Phase 2 — Factory plumbing + per-collection override + relaxed gate ✅

- [x] **2.1** Discovered mid-implementation: the strict-TTL enforcement is a **client-side throw** in `ReleaseAsync` (line 854), not a Mongo filter. The Mongo commit filter already only checks `Lock.LockKey == ours` — the database itself is happy to accept expired commits. So no Mongo-filter touchpoints; just the throw needed relaxing.
- [x] **2.2** Plumbed `AllowDelayedCommit` through `IMongoDbServiceFactory` (with concrete `MongoDbServiceFactory` setting it from `DatabaseOptions.AllowDelayedCommit` at registration). Mirrors how `SourceName` is exposed today. Avoids adding `IOptions<DatabaseOptions>` to every collection's constructor.
- [x] **2.3** Added `protected virtual bool AllowDelayedCommit => _mongoDbServiceFactory.AllowDelayedCommit;` on `LockableRepositoryCollectionBase<TEntity, TKey>` — defaults to the global value, overridable per collection.
- [x] **2.4** Modified the throw gate at `ReleaseAsync` line 854 from `(commit || exception != null) && expired` to `expired && (exception != null || (commit && !AllowDelayedCommit))`. Exception-release stays strict; commit becomes opt-in-strict.
- [x] **2.5** Modified the completion-callback gate at line 891 from `!expired` to `(!expired || commit)` — the delayed-commit path successfully wrote a doc, so the completion callback should fire.

### Phase 3 — Log line ✅

- [x] **3.1** `LogInformation` emitted when the commit is delayed (expired + commit + AllowDelayedCommit). Structured fields: `{entityId}`, `{collection}`, `{expiredBy}` (TimeSpan).

### Phase 4 — Tests ✅

- [x] **4.1** New `DelayedCommitTests` covers: delayed-commit succeeds when no other writer, per-collection override strict-mode throws, global-factory strict-mode throws, on-time commits unchanged.
- [x] **4.2** Existing tests that encoded the OLD strict-TTL contract updated to match the new behaviour:
  - `PickTests.PickAndCommitTooLate` (Update + Delete) — was "throws LockExpired", now "succeeds + verifies the write".
  - `PickTests.PickAndCommitTooLateThenTryToSetException` — preliminary "commit fails" became "commit succeeds"; subsequent SetException still throws `LockAlreadyReleasedException` (scope-already-released guard unchanged).
  - `PickTests.PickAndCommitTooLateWhenOtherHavePicked` — exception type changed from `LockExpiredException` to `InvalidOperationException` ("Cannot find entity before release" from the LockKey mismatch).
  - `ReleaseUpdateTests.ReleaseEntityWithExpiredLock` + `ReleasEntityLockedByOtherScope` — Commit now succeeds (and fires completion callback); SetErrorState still throws; Abandon unchanged.
  - `ReleaseDeleteTests.ReleaseEntityWithExpiredLock` + `ReleasEntityLockedByOtherScope` — same shape; the delayed-commit path correctly deletes the document.
- [x] **4.3** Time control: tests use `TimeSpan.Zero` (immediate expiry) for the deterministic paths and `Task.Delay` only for the `WhenOtherHavePicked` race scenario which fundamentally needs real-time progression.
- [x] **4.4** Full suite: 455 passed, 5 Lockable transaction-test failures — same pre-existing flaky cohort observed on bare master.

### Phase 5 — Close-out

- [ ] **5.1** Single commit per recent pattern.
- [ ] **5.2** Push.
- [ ] **5.3** Archive plan to `done/lockable-delayed-commit.md`, update `planned/README.md`, `git rm -r plan`, final commit `feat: lockable-delayed-commit complete`, push, open PR referencing the `MongoDB.md` backlog item.

## Last session

Plan finalised with user's choices: `AllowDelayedCommit` toggle, default `true`, settable on `DatabaseOptions` (global) and overridable via virtual property on `LockableRepositoryCollectionBase<TEntity, TKey>`. The per-event `CommittedAfterExpiry` flag on the summary record is dropped from scope — log line is enough for v1. Awaiting go-ahead to start Phase 1.
