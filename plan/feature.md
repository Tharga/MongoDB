# Feature: Lockable `ExecuteAsync` allows `Operation.Create`

Closes [#109](https://github.com/Tharga/MongoDB/issues/109) on merge.

## Goal

Relax `LockableRepositoryCollectionBase.ExecuteAsync`'s guard so it accepts `Operation.Create` in addition to `Operation.Read`. This is the small change that unblocks `col.Indexes.CreateOneAsync(...)` (and friends) on lockable collections — without inventing a new `IndexAsync` API or `Operation.Index` enum value.

The reasoning (per the user's design call on the issue): `Update` and `Delete` against existing documents could clobber active locks, so they must go through the lock-acquire/commit cycle. `Create` only adds brand-new documents (with `Lock = null`) or runs DDL like index creation — neither touches an existing lock. `AddAsync` on `LockableRepositoryCollectionBase` already delegates straight to `Disk.AddAsync` with `Operation.Create`, so allowing `Create` via `ExecuteAsync` is consistent with what the lockable surface already does.

## Background

`LockableRepositoryCollectionBase.ExecuteAsync` (both overloads, lines 264 + 270) currently throws if `operation != Operation.Read`. That's a deliberate guard to prevent ad-hoc document writes that bypass the lock contract.

But the guard also rejects index-management operations:

```csharp
// InvalidOperationException: Only operation Read is allowed for lockable repository collections.
await myLockableCollection.ExecuteAsync(async col =>
{
    await col.Indexes.CreateOneAsync(new CreateIndexModel<MyEntity>(
        Builders<MyEntity>.IndexKeys.Ascending("SomeField"),
        new CreateIndexOptions { Unique = true, Name = "SomeField" }));
    return true;
}, Operation.Create);
```

Consumers (Eplicta's EP-4156 unique-index reconcile in particular) currently work around this by passing `Operation.Read`, which is a lie about intent.

## Scope

### 1. Relax the guard

Both `ExecuteAsync` overloads in `Tharga.MongoDB/Lockable/LockableRepositoryCollectionBase.cs` change from:

```csharp
if (operation != Operation.Read) throw new InvalidOperationException(
    $"Only operation {nameof(Operation.Read)} is allowed for lockable repository collections.");
```

to:

```csharp
if (operation != Operation.Read && operation != Operation.Create) throw new InvalidOperationException(
    $"Only operations {nameof(Operation.Read)} and {nameof(Operation.Create)} are allowed for lockable repository collections. " +
    "Update and Delete must go through the lock-acquire/commit cycle.");
```

`ExecuteManyAsync` has no guard today (it doesn't take an `Operation`) and stays unchanged.

### 2. Update the consumer-facing docs on `IRepositoryCollection.ExecuteAsync`

If the existing XML doc on the lockable side calls out "Read only", refresh it to "Read or Create — see ExecuteAsync rejecting Update/Delete." Verify at impl time which doc comments exist and refresh as needed.

### 3. Tests

Add to `Tharga.MongoDB.Tests` (likely in or adjacent to the existing lockable-collection test file):

- `Lockable_ExecuteAsync_OperationRead_Allowed` — still allowed (existing behaviour).
- `Lockable_ExecuteAsync_OperationCreate_Allowed` — new; constructing or calling the lambda with `Operation.Create` no longer throws.
- `Lockable_ExecuteAsync_OperationUpdate_Throws` — still throws.
- `Lockable_ExecuteAsync_OperationDelete_Throws` — still throws.
- `Lockable_ExecuteAsync_IndexCreate_Works` (integration-y, against the test Mongo) — uses the issue's repro pattern (`col.Indexes.CreateOneAsync(...)`) to build a unique index on a lockable collection.

Verify the exception message in the Update/Delete tests to lock the contract.

## Out of scope

- **Adding `Operation.Index`** or a dedicated `IndexAsync(Func<IMongoIndexManager<TEntity>, ...>)` API. Reconsider if a consumer wants the strictest contract; the issue lists both options A + B and the user picked the simpler "allow Create" path.
- **Generic `IRepositoryCollection.IndexAsync`** on `IRepositoryCollection<TEntity, TKey>` and a `RepositoryCollectionBase` abstract method. Not needed for #109's repro since `ExecuteAsync(..., Operation.Create)` now suffices.
- **Telemetry/queue classification changes**. `Operation.Create` already flows through the existing monitor + limiter as a Create — the right thing happens by default.

## Acceptance criteria

- `LockableRepositoryCollectionBase.ExecuteAsync(..., Operation.Create)` succeeds. The repro in the issue (index creation) works against a real Mongo.
- `LockableRepositoryCollectionBase.ExecuteAsync(..., Operation.Update)` throws `InvalidOperationException` with a message naming `Update` and pointing at the lock-acquire/commit cycle.
- `LockableRepositoryCollectionBase.ExecuteAsync(..., Operation.Delete)` throws the same way.
- All four behaviours covered by new tests.
- Existing tests stay green (modulo the pre-existing Lockable transaction-test flaky cohort).

## Done condition

- Acceptance criteria met.
- Plan archived to `done/lockable-execute-allow-create.md`; `planned/README.md` updated.
- PR opens with `closes #109`.

## Effort

Small. ~3 lines of guard-relaxation + 4-5 tests. Ships in a single commit.

## NuGet

Current. No bumps needed.
