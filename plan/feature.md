# Feature: Generalised document lock with commit-time Update/Delete decision

## Originating Branch
`feature/generalized-document-lock` (off master, 2026-05-03)

## Source
[`$DOC_ROOT/Tharga/plans/Toolkit/MongoDB/planned/30-generalized-document-lock.md`](../../../../Users/danie/SynologyDrive/Documents/Notes/Tharga/plans/Toolkit/MongoDB/planned/30-generalized-document-lock.md), originally requested from the Berakna project (April 2026).

This feature also closes [planned/31](../../../../Users/danie/SynologyDrive/Documents/Notes/Tharga/plans/Toolkit/MongoDB/planned/31-refactor-lock-for-update-delete.md): *Refactor `LockForUpdate` / `LockForDelete` as wrappers over #30* — the refactor falls out naturally from extracting the shared acquire-lock primitive.

> **Dependency note (resolved):** the original spec said this depends on #29 (transactions). We agreed that's a design choice, not a constraint. This feature ships with **sequential commit** for the multi-doc case. A small follow-up overload (`CommitAsync(IClientSessionHandle session)`) can light up *all-or-nothing* semantics later, once #29 lands.

## Goal
Add two new lock entry points that delay the update-vs-delete decision until commit time:

1. **Single-doc `LockAsync`** — locks one document; on commit the caller passes a mode (`Update` / `Delete`).
2. **Multi-doc `LockManyAsync`** — locks N documents from one collection; the lease lets the caller stage per-document decisions and apply them all on a single `CommitAsync`.

Both share an internal `AcquireLockAsync` primitive. `PickForUpdateAsync` / `PickForDeleteAsync` are reimplemented as thin wrappers over that same primitive, keeping their public API and behavior unchanged (backward-compatible).

## Scope

### New public types

```csharp
namespace Tharga.MongoDB.Lockable;

public enum LockCommitMode
{
    Update,
    Delete,
}

// Single-doc unified lock scope (Phase 1)
public record LockScope<TEntity, TKey> : IAsyncDisposable, IDisposable
    where TEntity : LockableEntityBase<TKey>
{
    public TEntity Entity { get; }
    public Task CommitAsync(LockCommitMode mode, TEntity updatedEntity = null);
    public Task AbandonAsync();
    public Task SetErrorStateAsync(Exception exception);
}

// Multi-doc lease (Phase 2)
public sealed class DocumentLease<TEntity, TKey> : IAsyncDisposable
    where TEntity : LockableEntityBase<TKey>
{
    public IReadOnlyList<TEntity> Documents { get; }

    public void MarkForUpdate(TEntity updated);            // id taken from updated.Id
    public void MarkForUpdate(TKey id, TEntity updated);
    public void MarkForDelete(TKey id);
    public void MarkRelease(TKey id);                      // explicit no-op (default)

    public Task<DocumentLeaseCommitSummary> CommitAsync(CancellationToken cancellationToken = default);
    public ValueTask DisposeAsync();                       // releases all not-yet-marked locks
}

public record DocumentLeaseCommitSummary
{
    public required int Updated { get; init; }
    public required int Deleted { get; init; }
    public required int ReleasedUnchanged { get; init; }
    public required IReadOnlyList<DocumentLeaseFailure> Failures { get; init; }
}

public record DocumentLeaseFailure
{
    public required object Id { get; init; }
    public required LockCommitMode? IntendedDecision { get; init; }   // null = release
    public required string Error { get; init; }
}
```

### New API on `ILockableRepositoryCollection<TEntity, TKey>`

```csharp
// Single-doc
Task<LockScope<TEntity, TKey>> LockAsync(TKey id, TimeSpan? timeout = null, string actor = null, CancellationToken cancellationToken = default);
Task<LockScope<TEntity, TKey>> LockAsync(FilterDefinition<TEntity> filter, TimeSpan? timeout = null, string actor = null, CancellationToken cancellationToken = default);
Task<LockScope<TEntity, TKey>> LockAsync(Expression<Func<TEntity, bool>> predicate, TimeSpan? timeout = null, string actor = null, CancellationToken cancellationToken = default);

// Multi-doc
Task<DocumentLease<TEntity, TKey>> LockManyAsync(IEnumerable<TKey> ids, TimeSpan? timeout = null, string actor = null, CancellationToken cancellationToken = default);
Task<DocumentLease<TEntity, TKey>> LockManyAsync(FilterDefinition<TEntity> filter, TimeSpan? timeout = null, string actor = null, CancellationToken cancellationToken = default);
Task<DocumentLease<TEntity, TKey>> LockManyAsync(Expression<Func<TEntity, bool>> predicate, TimeSpan? timeout = null, string actor = null, CancellationToken cancellationToken = default);
```

### Backward-compatible refactor of existing API
- `PickForUpdateAsync` (all overloads + `completeAction` parameter) → unchanged signatures, unchanged returned `EntityScope<TEntity, TKey>` type, unchanged behavior. Internally rerouted through the new shared `AcquireLockAsync` helper.
- `PickForDeleteAsync` (same).
- `LockEvent` continues to fire from the shared helper — same event for all entry points (`LockAsync`, `LockManyAsync`, legacy `Pick*`).

### Acquisition strategy (multi-doc)
- **Sequential acquisition with deterministic ordering by key** (sorted via `Comparer<TKey>.Default`) so two leases targeting overlapping sets always lock in the same order — no AB / BA deadlocks.
- If any acquisition fails, **release all locks acquired so far** and surface the original failure. The lease never returns half-acquired.
- Filter / predicate overloads resolve to an id list first, then delegate to the id-list overload (single acquire path).

### Commit strategy (multi-doc)
- **Sequential application** in mark order. Each per-document operation reuses the same single-doc commit primitive shared with `LockAsync`.
- A failure mid-commit is **collected, not propagated immediately**. The summary's `Failures` list reports each failed decision; remaining decisions still attempt to apply (consistent with the existing locks' partial-failure model). Locks for failed operations remain locked and will eventually expire, same as today.

### Disposal
- `LockScope.DisposeAsync` without a prior commit → calls `AbandonAsync` (matches existing `EntityScope` behavior).
- `DocumentLease.DisposeAsync` → releases all unmarked locks via the same path.

### Implementation outline
- **Step A (single-doc + refactor):** Pull the atomic acquire-lock body out of `PickForUpdateAsync(id)` into a private `AcquireLockAsync(id, timeout, actor, ...)` returning the locked entity + a release primitive. Repoint `PickForUpdateAsync` and `PickForDeleteAsync` to call it. Implement `LockAsync` on top of the same helper, returning a new `LockScope` whose release dispatches on `LockCommitMode`.
- **Step B (multi-doc):** Implement `LockManyAsync` by calling `AcquireLockAsync` per id (sorted), wrapping the N scopes in a `DocumentLease`. The lease's `CommitAsync` iterates staged decisions and dispatches to the same per-doc update/delete paths used in Step A.

## Out of scope (deferred)
- **Atomic commit via transactions** — `lease.CommitAsync(IClientSessionHandle session)` overload becomes available once #29 lands. Filed as a follow-up.
- **Cross-collection leases** — different entity types in one lease. Out of scope until consumer-driven.
- **Async deadlock detection** for two leases that lock overlapping sets in *different* orders. The deterministic key-sort mitigates this; explicit detection isn't worth it for now.
- **Read-only locks** — out of scope per the original spec (use a snapshot read instead).
- **Deprecating `PickForUpdateAsync` / `PickForDeleteAsync`** — they remain first-class public API. They're idiomatic for the "I know the outcome up front" case.

## Acceptance criteria

### Single-doc + refactor
- [ ] `LockCommitMode` enum + `LockScope<TEntity, TKey>` record under `Tharga.MongoDB.Lockable`
- [ ] 3 `LockAsync` overloads (id / filter / predicate) on `ILockableRepositoryCollection<TEntity, TKey>`
- [ ] Internal `AcquireLockAsync` helper used by `LockAsync`, `PickForUpdateAsync`, `PickForDeleteAsync`, and (Step B) `LockManyAsync`
- [ ] `LockEvent` fires from the shared helper for all entry points
- [ ] `CommitAsync(Update, updated?)` and `CommitAsync(Delete)` work and clear the lock; `AbandonAsync` releases unchanged
- [ ] **Regression: all existing `Lockable` tests pass without modification.** This is the backward-compat checkpoint.

### Multi-doc
- [ ] `DocumentLease<TEntity, TKey>` exposes `Documents`, `MarkForUpdate`, `MarkForDelete`, `MarkRelease`, `CommitAsync`, `DisposeAsync`
- [ ] 3 `LockManyAsync` overloads (id list / filter / predicate)
- [ ] Acquisition is sequential and ordered by key; any failure rolls back partially-acquired locks
- [ ] `CommitAsync` applies decisions sequentially; collects failures into `DocumentLeaseCommitSummary.Failures`
- [ ] `DisposeAsync` without `CommitAsync` releases all locks

### Cross-cutting
- [ ] New tests cover both single-doc and multi-doc cases (see plan.md Step 5 for the matrix)
- [ ] README documents both entry points with examples
- [ ] Build + tests green on net8/9/10 within the 50-warning budget

## Done condition
Consumers can:
- Call `LockAsync(id)`, inspect the entity, and decide at commit time whether to update (with optional changes) or delete.
- Call `LockManyAsync([ids])`, inspect each entity, stage per-document decisions, and apply them with a single `CommitAsync`.

The existing `PickForUpdate` / `PickForDelete` lock primitives keep working unchanged — they're now thin wrappers over the same shared acquire-lock primitive. This feature closes the substance of original `planned/30` and all of `planned/31`. Remaining follow-ups are **transactional commit** (depends on #29) and **cross-collection leases** (consumer-driven).
