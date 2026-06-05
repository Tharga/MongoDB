# Plan: Lockable `ExecuteAsync` allows `Operation.Create`

Feature scope: see [feature.md](feature.md). Branch: `feature/lockable-execute-allow-create`. Closes #109.

## Steps

### Phase 1 — Relax the guard

- [ ] **1.1** Update `LockableRepositoryCollectionBase.ExecuteAsync` (both overloads, lines 264 + 270) to accept `Operation.Read` OR `Operation.Create`. Update the exception message to name `Update`/`Delete` as the rejected ops and point at the lock cycle.
- [ ] **1.2** Refresh any XML doc comments that say "Read only" — verify on the interface (`IRepositoryCollection<,>.ExecuteAsync`) and on the lockable override.

### Phase 2 — Tests

- [ ] **2.1** Locate the existing lockable test file (likely `Tharga.MongoDB.Tests/Lockable*.cs`) — add the new tests adjacent or in a new `LockableExecuteAsyncGuardTests.cs` if there's no natural home.
- [ ] **2.2** `OperationRead_Allowed` — unchanged behaviour, sanity check that the lambda runs.
- [ ] **2.3** `OperationCreate_Allowed` — new, asserts no throw and the lambda runs.
- [ ] **2.4** `OperationUpdate_Throws` + `OperationDelete_Throws` — assert `InvalidOperationException` and message naming the rejected op.
- [ ] **2.5** `IndexCreate_Works` — integration-y, runs against the test Mongo: build a unique index on a lockable collection via the issue's repro shape. Verify the index appears in `Indexes.List()`.

### Phase 3 — Close-out

- [ ] **3.1** Single cohesive commit.
- [ ] **3.2** Push.
- [ ] **3.3** Archive plan to `done/lockable-execute-allow-create.md`, update `planned/README.md` Done section, `git rm -r plan`, final commit `feat: lockable-execute-allow-create complete`, push, open PR closing #109.

## Last session

Plan finalised after issue review + design discussion. User chose the smallest-possible change: allow `Operation.Create` (not just Read) through `LockableRepositoryCollectionBase.ExecuteAsync`. Update/Delete stay blocked (they can clobber active locks). Awaiting go-ahead to start Phase 1.

## Open questions worth flagging at impl

- Verify what XML doc comments currently exist on `IRepositoryCollection.ExecuteAsync` (interface) and the lockable override. If neither has a "Read only" comment, no doc work needed.
- Confirm there are no other internal callers of `LockableRepositoryCollectionBase.ExecuteAsync` that currently pass `Operation.Create` with an expectation of throwing.
