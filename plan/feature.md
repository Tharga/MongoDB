# Feature: Multi-document transactions

## Originating Branch
`feature/transactions` (off master, 2026-05-04)

## Source
[`$DOC_ROOT/Tharga/plans/Toolkit/MongoDB/planned/29-transactions.md`](../../../../Users/danie/SynologyDrive/Documents/Notes/Tharga/plans/Toolkit/MongoDB/planned/29-transactions.md), originally requested from the Berakna project (April 2026).

## Goal
Let callers execute multiple writes — across one or more collections, mixed Disk and Lockable, on the same MongoDB cluster — inside a single atomic unit of work. On success the unit commits; on exception or explicit abort the unit rolls back.

## Scope

### High-level shape

Two layered APIs:

**(a) Low-level — explicit session, mixed Disk + Lockable inside one transaction:**
```csharp
await mongoDbServiceFactory.WithTransactionAsync(async session =>
{
    await jobsRepo.AddAsync(job, session);
    await statsRepo.AddAsync(stat, session);
    await using var lockedScope = await accountRepo.LockAsync(accountId, session: session);
    await lockedScope.CommitAsync(CommitMode.Update, updatedAccount);
});
```

**(b) High-level — atomic multi-doc commit, no session boilerplate (most common case):**
```csharp
await using var lease = await coll.LockManyAsync(ids);

foreach (var doc in lease.Documents) { /* MarkForUpdate / MarkForDelete / leave unmarked */ }

var summary = await lease.CommitAsync(transactional: true);    // <-- atomic commit pass
```

- One entry point: `WithTransactionAsync(Func<IClientSessionHandle, Task> body)` (and a generic `<TResult>` variant). It opens a session, starts a transaction, runs the body, commits on success / aborts on exception.
- Every write method on `IRepositoryCollection<TEntity, TKey>` and `ILockableRepositoryCollection<TEntity, TKey>` gets a session-aware variant — the session is threaded all the way down to the driver call.
- `DocumentLease.CommitAsync(bool transactional = false)` ships in the same feature: when `true`, the commit pass runs inside an internal transaction so all marked decisions land atomically (or none do). Default stays the existing sequential / partial-failure semantics.
- Mixed collections work: a transaction can contain Disk writes + Lockable lock acquisitions + Lockable commits, all atomic.

#### Why the transactional flag is on `CommitAsync`, not on `LockManyAsync`
Acquisition is fast (microseconds per doc); commit is where atomicity matters. Wrapping the *whole* lease — acquire, consumer-think, commit — in one transaction risks blowing past Mongo's 60-second transaction timeout while the consumer is still inspecting data. The flag at commit time keeps acquisition unchanged and only wraps the commit pass, which is microseconds.

### Why explicit session, not "ambient session"

Considered an `AsyncLocal<IClientSessionHandle>` "ambient session" approach where any write under a `WithTransactionAsync` block would automatically pick up the session — no API changes. Rejected because:
- Hidden state makes debugging "why did this insert leak out of the transaction?" much harder.
- Breaks down with parallel `Task.WhenAll(...)` of mixed-cluster writes.
- Less honest about which calls participate.

Explicit session passing matches the MongoDB driver's own model and consumer expectations. Trade-off: wider API surface (every write method gains a `session` parameter or overload).

### Write surface that needs session support

#### `DiskRepositoryCollectionBase<TEntity, TKey>` (and its base + interface)
Every write method threads `IClientSessionHandle session = null` through to the driver call:
- `AddAsync(entity, session)` / `TryAddAsync(entity, session)` / `AddManyAsync(entities, session)`
- `AddOrReplaceAsync(entity, session)`
- `ReplaceOneAsync(entity, session)` / `ReplaceOneAsync(entity, filter, options, session)`
- `UpdateOneAsync(id, update, session)` / `UpdateOneAsync(predicate/filter, update, options, session)`
- `UpdateManyAsync(predicate/filter, update, session)`
- `DeleteOneAsync(id, session)` / `DeleteOneAsync(predicate/filter, options, session)`
- `DeleteManyAsync(predicate/filter, session)`
- The protected helper `ExecuteAsync<T>(name, action, operation, session, ...)` — the central funnel — gains a `session` parameter and forwards it to the lambda.

Reads inside a transaction also need session for snapshot consistency. Adding session to `GetAsync` / `GetOneAsync` / `CountAsync` etc. as a follow-up — write-only is sufficient for the Berakna primary use case (atomic writes).

#### `LockableRepositoryCollectionBase<TEntity, TKey>`
Acquire-lock and commit paths route through `Disk.*` writes, so making Disk session-aware auto-propagates. We add:
- `LockAsync(id|filter|predicate, ..., session)` overloads — session attached to the locked scope so commit happens inside the transaction.
- `LockManyAsync(ids|filter|predicate, ..., session)` overloads — session attached to the lease for both acquisition and commit.
- Backward compat: existing `PickForUpdateAsync` / `PickForDeleteAsync` get session-aware overloads too. The existing parameterless overloads keep working.
- The internal `AcquireLockAsync(filter, timeout, actor, failIfLocked, session)` carries the session through.
- The release-action lambdas captured by `LockScope` and `DocumentLease` close over the session, so commit/abandon happens inside the transaction.

#### `DocumentLease<TEntity, TKey>` — `CommitAsync(transactional: true)`
- New optional parameter `bool transactional = false` on `DocumentLease<TEntity, TKey>.CommitAsync`. Default keeps today's sequential commit + partial-failure semantics — no behavior change for existing callers.
- When `transactional` is `true`, the commit pass internally calls `WithTransactionAsync` and applies all staged decisions inside a single transaction. If any decision fails, the whole pass aborts and **no decisions land** (true all-or-nothing). The locks remain held — the consumer can retry, abandon, or expire them.
- The summary returned in the transactional mode reports `Updated` / `Deleted` / `ReleasedUnchanged` counts as before; `Failures` is empty on success or carries the single triggering failure on abort.
- The lease's session-binding (when constructed inside a `WithTransactionAsync` block) is respected: if the lease was created with a session, `CommitAsync(transactional: true)` uses *that* session — no nested transaction. If no session is bound, it opens a fresh one.

### `WithTransactionAsync` entry point

Lives on a new `IMongoDbTransactionContext` (or a method group on `IMongoDbServiceFactory`) — TBD in step 1 implementation. The body receives an `IClientSessionHandle`. The wrapper:
1. Resolves the `MongoClient` for a given configuration (default: the default config; optional overload for named config).
2. Calls `client.StartSessionAsync()`.
3. Calls `session.WithTransactionAsync(body)` — the driver handles transient-error retries and commit/abort.
4. Surfaces transaction-related exceptions clearly (e.g. `MongoNotPrimaryException` if not on a replica set).

Cross-cluster transactions are explicitly rejected: if a caller passes a session bound to one client into a write on a collection from a different client, MongoDB throws — we let that surface.

### Index assurance and transactions

`OperationIndexManagement` runs inside `ExecuteAsync<T>` *before* the action and may issue DDL (index creation). MongoDB **forbids index DDL inside a transaction** — calling `CreateIndexes` inside a transaction throws. So when a session is present, `ExecuteAsync<T>` skips the index-assurance step (relying on the existing per-collection one-time initialization to have already run before the transaction starts). Explicit assertion in code: skip + log if index assurance is needed but a session is active.

### Out of scope (deferred follow-ups)

- **Transaction-grouped call tracking in the admin UI** — adding a `TransactionId` field to `CallDto` and grouping calls in the Blazor admin/MCP. A separate feature; ripples through `Tharga.MongoDB.Mcp` and Monitor.Server. The acceptance criterion is partially met by current call tracking (transactional calls still appear, just not grouped).
- **Distributed transactions across different MongoDB clusters** — not supported by MongoDB.
- **Nested transactions / savepoints** — MongoDB doesn't support these.
- **Long-running transactions** — MongoDB's 60-second default stands; we don't try to work around it.
- **Session-aware reads** (`GetAsync(filter, session)` etc.) — useful for read-your-own-writes within a transaction, but write atomicity is the primary goal here. Filed as follow-up.

## Acceptance criteria
- [ ] `WithTransactionAsync(Func<IClientSessionHandle, Task>)` and `<TResult>` overload exposed on a service obtainable from DI
- [ ] All `IRepositoryCollection<TEntity, TKey>` write methods accept an optional `IClientSessionHandle` and forward it to the driver
- [ ] All `ILockableRepositoryCollection<TEntity, TKey>` lock methods (`LockAsync`, `LockManyAsync`, `PickForUpdateAsync`, `PickForDeleteAsync`) accept an optional `IClientSessionHandle`; the resulting scope/lease commits inside the transaction
- [ ] `DocumentLease<TEntity, TKey>.CommitAsync(bool transactional = false)` — when `true`, applies all staged decisions atomically. Default preserves today's sequential / partial-failure semantics.
- [ ] Multi-collection commit: a transaction across two Disk collections + one Lockable collection commits atomically; a thrown exception aborts cleanly
- [ ] Index assurance is skipped when a session is active (explicit log, doesn't crash)
- [ ] Tests cover: multi-collection commit, rollback on exception, lockable inside transaction, mixed Disk + Lockable inside transaction, `CommitAsync(transactional: true)` happy path, `CommitAsync(transactional: true)` aborts on a single failed decision (no decisions land), calling session-aware ops without a session works (parameter is optional), index assurance skipped with active session
- [ ] README documents the primitive with: one Disk-only example, one mixed Disk+Lockable example, one `LockManyAsync` + `CommitAsync(transactional: true)` example
- [ ] Build + tests green on net8/9/10 within the 50-warning budget
- [ ] **Regression: all existing Disk + Lockable tests pass without edits.** Refactoring the central `ExecuteAsync<T>` funnel is the riskiest part — this is the gate.

## Done condition
A consumer can call `WithTransactionAsync(...)` and inside the block do mixed writes on one or more Disk and Lockable collections — including taking and committing locks — with MongoDB-level atomic guarantees. Passing the session is explicit; methods that don't take a session continue to work as before.

For the most common multi-doc atomic-commit case, the consumer doesn't have to touch `IClientSessionHandle` at all: `LockManyAsync(ids)` followed by `CommitAsync(transactional: true)` does the right thing — acquisition stays sequential and fast, commit becomes all-or-nothing.

Closes planned/29 *and* the previously-filed follow-up "Transactional commit overload on `DocumentLease`" (which was waiting on this feature). One feature, both layers.
