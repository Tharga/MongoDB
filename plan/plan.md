# Plan: Multi-document transactions

## Steps

### Step 1: Audit (already done in pre-plan)
- [x] `IRepositoryCollection<TEntity, TKey>` and `ILockableRepositoryCollection<TEntity, TKey>` interfaces — write surface enumerated
- [x] `DiskRepositoryCollectionBase<TEntity, TKey>.ExecuteAsync<T>` is the central funnel for every write
- [x] Lockable's `AcquireLockAsync` / release paths route through `Disk.*` writes
- [x] No pre-existing transaction or session infrastructure
- [x] `MongoDbClientProvider.GetClient(MongoUrl)` caches per `MongoUrl` — sessions live within one client

### Step 2: Decide the entry-point shape — DONE
- [x] **Entry-point home**: extension method on `IMongoDbServiceFactory`, in a new static class `MongoDbServiceFactoryTransactionExtensions`. Lighter than a dedicated `IMongoDbTransactionService`; consumers already have the factory; we can add a dedicated service later without breaking this API.
- [x] **Signatures**:
  ```csharp
  Task WithTransactionAsync(
      this IMongoDbServiceFactory factory,
      Func<IClientSessionHandle, CancellationToken, Task> body,
      string configurationName = null,
      TransactionOptions options = null,
      CancellationToken cancellationToken = default);

  Task<TResult> WithTransactionAsync<TResult>(
      this IMongoDbServiceFactory factory,
      Func<IClientSessionHandle, CancellationToken, Task<TResult>> body,
      string configurationName = null,
      TransactionOptions options = null,
      CancellationToken cancellationToken = default);
  ```
- [x] **Session-creation hook**: add `Task<IClientSessionHandle> StartSessionAsync(ClientSessionOptions options = null, CancellationToken cancellationToken = default)` to `IMongoDbService`. Implementation in `MongoDbService` calls `_mongoClient.StartSessionAsync(...)`. Avoids exposing `MongoClient` publicly.
- [x] **Error behavior**: body exceptions abort + rethrow. Driver retries transient transaction errors automatically. Standalone-Mongo failures surface as the driver's `MongoCommandException` (code 20). Cancellation forwarded.
- [x] **Cross-config behavior**: caller picks one configuration as the transaction's anchor; writes against a different `MongoClient` throw at the driver level (we don't preempt with a client-side check). Two configs pointing at the same cluster but with different `MongoUrl` resolve to different cached `MongoClient`s — sessions don't transfer; documented as a known limitation.
- [x] Build clean as starting point — already verified in pre-plan

### Step 3: Refactor `ExecuteAsync<T>` to thread `IClientSessionHandle`
- [ ] Add `IClientSessionHandle session = null` parameter to `DiskRepositoryCollectionBase<TEntity, TKey>.ExecuteAsync<T>` (the protected funnel)
- [ ] Change the action lambda from `Func<IMongoCollection<TEntity>, CancellationToken, Task<(T, int)>>` to `Func<IMongoCollection<TEntity>, IClientSessionHandle, CancellationToken, Task<(T, int)>>` so each write site can pick the session-aware driver overload
- [ ] When `session != null`, **skip `OperationIndexManagement`** and log "skipping index assurance — transaction active" at debug. (Index DDL inside a Mongo transaction throws.)
- [ ] Update every internal call site of `ExecuteAsync` in `DiskRepositoryCollectionBase` to take the new third lambda parameter (most just ignore it)
- [ ] **Run all existing tests.** Behavior must be identical for the no-session path. Regression gate.
- [ ] Build clean

### Step 4: Add session-aware overloads on Disk write methods
- [ ] `AddAsync(TEntity entity, IClientSessionHandle session = null)` — single new overload via optional parameter (source-compat)
- [ ] Same for `TryAddAsync`, `AddManyAsync`, `AddOrReplaceAsync`, `ReplaceOneAsync` (both signatures), `UpdateOneAsync` (3 signatures), `UpdateManyAsync` (2 signatures), `DeleteOneAsync` (3 signatures), `DeleteManyAsync` (2 signatures)
- [ ] Mirror the new optional parameter on `IRepositoryCollection<TEntity, TKey>` so consumers see it
- [ ] Each impl threads `session` to the underlying driver method via the lambda's third parameter (e.g. `collection.InsertOneAsync(sess, entity, ct)` when `sess != null`, else the no-session overload — small switch helper extension `Inv(...)` to keep call sites concise)
- [ ] **Run all existing tests.** No-session calls must behave identically. Regression gate.
- [ ] Build clean

### Step 5: Add session support to Lockable
- [ ] Internal `AcquireLockAsync` gains `IClientSessionHandle session = null`. When set, the atomic `Disk.UpdateOneAsync(...)` and the follow-up `GetAsync(filter)` both run under the session.
- [ ] `BuildLockScope` and `DocumentLease` capture the session and use it for commits (`PrepareCommitForUpdateAsync`, `PerformCommitForDeleteAsync`, abandon path) — i.e. the per-doc release-action lambdas all forward the session to `Disk.ReplaceOneAsync` / `Disk.DeleteOneAsync` / `Disk.UpdateOneAsync`.
- [ ] New session-aware overloads on `ILockableRepositoryCollection<TEntity, TKey>`:
  - `LockAsync(id|filter|predicate, ..., IClientSessionHandle session = null)`
  - `LockManyAsync(ids|filter|predicate, ..., IClientSessionHandle session = null)`
  - `PickForUpdateAsync(id|filter|predicate, ..., IClientSessionHandle session = null)`
  - `PickForDeleteAsync(id|filter|predicate, ..., IClientSessionHandle session = null)`
- [ ] **Regression: all 128 existing Lockable tests pass without edits.** Gate.
- [ ] Build clean

### Step 6: Implement `WithTransactionAsync`
- [ ] Per the choice in Step 2, add the entry point. Likely: register a service on the DI container that holds the `IMongoDbServiceFactory` + a way to resolve the `MongoClient` for a given configuration (currently happens inside `MongoDbService` via `_mongoDbServiceFactory` → `MongoDbClientProvider`)
- [ ] Implementation calls `client.StartSessionAsync()`, then `session.WithTransactionAsync(body)`. The driver handles transient retries.
- [ ] Surface a clear error if the cluster doesn't support transactions (typically `MongoNotPrimaryException` or similar — test against standalone Mongo to confirm what surfaces)
- [ ] Build clean

### Step 7: Add `CommitAsync(bool transactional = false)` to `DocumentLease`
- [ ] Add the optional parameter to `DocumentLease<TEntity, TKey>.CommitAsync`. Default `false` preserves the existing sequential / partial-failure path verbatim.
- [ ] When `transactional == true`:
  - If the lease was constructed with a session (low-level path: caller already inside `WithTransactionAsync`), reuse that session. No nested transaction.
  - Otherwise, internally call `WithTransactionAsync(...)` and apply all staged decisions inside the transaction.
- [ ] On a single staged decision failing inside the transactional pass, abort the transaction and surface the failure. The summary returned has `Updated`/`Deleted`/`ReleasedUnchanged` = 0 and `Failures` carrying the triggering failure (or rethrow — decide based on whether the existing collected-failure model should still apply; lean toward surfacing failure as a thrown exception in the transactional path, since the all-or-nothing intent is clearer that way).
- [ ] Locks remain held on transactional abort — consumer can retry, abandon, or let them expire.
- [ ] Build clean

### Step 8: Tests
- [ ] **Multi-collection happy path**: open a transaction, write to two Disk collections, commit, verify both writes landed
- [ ] **Rollback on exception**: open a transaction, write to two Disk collections, throw inside the body, verify *neither* write landed (atomic abort)
- [ ] **Lockable inside transaction**: open a transaction, `LockAsync(id, session)`, mutate the entity, `CommitAsync(Update, updated)`, verify the write landed and the lock cleared atomically
- [ ] **Mixed Disk + Lockable**: one transaction containing both a Disk `AddAsync` and a Lockable `LockAsync`+`CommitAsync(Update)` — both succeed or both abort
- [ ] **Lockable rollback releases lock**: lock a doc inside a transaction, throw — the lock should not survive (abort means the lock-set update is rolled back too). Verify the doc is unlocked after.
- [ ] **No-session call still works**: every refactored method called without a session behaves exactly as before (mostly covered by the regression suite, but add one explicit test for confidence)
- [ ] **Index assurance skipped under session**: arrange a fresh collection that would normally trigger index creation; assert no index DDL fires when the call runs under a session
- [ ] **Replica-set guard**: against a standalone Mongo, calling `WithTransactionAsync` surfaces a clear exception (skip-if-not-replica-set or assert the exception type)
- [ ] **Lease transactional commit happy path**: `LockManyAsync(ids)` + `CommitAsync(transactional: true)` with mixed update/delete decisions — all decisions land, summary counts correct
- [ ] **Lease transactional commit aborts on failed decision**: arrange one decision to fail (e.g. update with stale entity that fails the lock-key filter) — verify *no* decisions land (atomicity); the locks remain held on the docs
- [ ] **Lease transactional commit reuses bound session**: lease constructed inside an outer `WithTransactionAsync` block + `CommitAsync(transactional: true)` — no nested transaction, all writes share the outer session
- [ ] **Regression**: full Lockable + Disk test suites pass without edits

### Step 9: Build verification on all targets
- [ ] Build on net8 / net9 / net10 — clean, warnings under 50 budget

### Step 10: README update
- [ ] New "Transactions" subsection explaining when transactions work (replica set / sharded), the `WithTransactionAsync` entry point, and:
  - one Disk-only example
  - one mixed Disk+Lockable example
  - one **`LockManyAsync` + `CommitAsync(transactional: true)`** example — the recommended path for "lock N docs, decide each, commit atomically" without touching `IClientSessionHandle`
- [ ] Note out-of-scope items (cross-cluster, savepoints, long-running)
- [ ] Cross-link from the existing Lockable section: "for atomic multi-doc with locks, see Transactions"

### Step 11: Milestone commit + closure
- [ ] (Per-step commits along the way; this is the closure commit)
- [ ] Archive `plan/feature.md` → `$DOC_ROOT/Tharga/plans/Toolkit/MongoDB/done/transactions.md`
- [ ] Delete `$DOC_ROOT/Tharga/plans/Toolkit/MongoDB/planned/29-transactions.md` (now superseded by archive)
- [ ] Update `$DOC_ROOT/Tharga/plans/Toolkit/MongoDB/planned/README.md`: drop #29 row; remove the "transactional commit overload on `DocumentLease`" follow-up bullet (now shipped as part of this feature); add bullet to "Done" recent list
- [ ] Delete `plan/` directory from the repo
- [ ] Final commit: `feat: transactions complete`

### Step 12: Push + PR
- [ ] User pushes `feature/transactions` to origin
- [ ] Claude opens PR to `Tharga/master`
- [ ] After merge — delete the feature branch (local + remote)

## Notes

### Risk: refactoring the central `ExecuteAsync<T>` funnel
This is the riskiest step (Step 3). Every write goes through it. The regression gate is the entire existing test suite passing without edits. If anything fails, back out and reconsider the lambda-signature change.

### Why session is a parameter, not ambient
See the rationale in `feature.md` § "Why explicit session, not ambient session". TL;DR: hidden state is a debugging hazard; explicit matches the driver's own model.

### Cross-cluster transactions
Not supported by MongoDB. We let the driver throw — no client-side detection. If a caller writes to two collections backed by different `MongoClient`s under one session, the second write throws `MongoCommandException` or similar with a "session not bound to this client" message.

### Index assurance + transactions
MongoDB forbids index DDL inside a transaction. `OperationIndexManagement` will throw if it tries to create an index under a session. Step 3 makes it skip when a session is active. Caveat: if the consumer's transaction is the *first* call against a fresh collection, the index won't get created until the next non-transactional call. That's acceptable — index assurance is a one-time-per-process concern, and consumers usually warm up the collection on startup.

## Last session
Plan drafted from `planned/29-transactions.md`. Branch `feature/transactions` created off master (`6604fe7`). Audit complete: writes funnel through `DiskRepositoryCollectionBase.ExecuteAsync<T>`, Lockable's lock paths route through `Disk.*` writes, no pre-existing transaction infrastructure.

Scope expanded to fold in the previously-filed follow-up "Transactional commit overload on `DocumentLease`" — it now ships in this feature as `DocumentLease<TEntity, TKey>.CommitAsync(bool transactional = false)`. Added Step 7 between `WithTransactionAsync` impl and tests, plus 3 new test cases.

Awaiting user confirmation before starting Step 2 (entry-point shape decision).
