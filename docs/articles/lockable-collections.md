# Lockable collections

`ILockableRepositoryCollection<TEntity, TKey>` is a write-protected collection — you can only update or delete documents by first taking an exclusive lock. Useful for queue-like workloads, long-running per-document workflows, and any case where two workers might race on the same record.

The lock is stored on the document itself (entity inherits `LockableEntityBase<TKey>`). It carries an actor name, an expiry timestamp, and an optional error state from a previous failed attempt. Locks auto-recover after expiry, so a crashed worker doesn't leave the document permanently stuck.

## Pick-style — decision known up front

When you already know whether you'll update or delete:

```csharp
await using var scope = await collection.PickForUpdateAsync(id);
scope.Entity.Data = "updated";
await scope.CommitAsync();        // writes the change and clears the lock

await using var del = await collection.PickForDeleteAsync(id);
await del.CommitAsync();          // deletes the document
```

Both accept `id`, `FilterDefinition<TEntity>`, or `Expression<Func<TEntity, bool>>` predicate, plus optional `timeout`, `actor`, and `completeAction` callback. Disposal without `CommitAsync` releases the lock unchanged.

## Unified lock — decide at commit time

When you need to inspect the document before deciding:

```csharp
await using var scope = await collection.LockAsync(id);
if (ShouldDelete(scope.Entity))
{
    await scope.CommitAsync(CommitMode.Delete);
}
else
{
    var updated = scope.Entity with { Data = "updated" };
    await scope.CommitAsync(CommitMode.Update, updated);
}
```

`AbandonAsync()` releases without changes. `SetErrorStateAsync(ex)` records an exception on the lock so retries can see what failed previously. Disposal without commit calls `AbandonAsync`. Same semantics as `PickFor*` — both go through the same internal acquire-lock primitive.

## Multi-document lease

To inspect several documents and decide each one's fate before committing them all:

```csharp
await using var lease = await collection.LockManyAsync(new[] { id1, id2, id3 });

foreach (var doc in lease.Documents)
{
    if (ShouldDelete(doc))
        lease.MarkForDelete(doc.Id);
    else if (HasChanges(doc))
        lease.MarkForUpdate(doc with { Data = "..." });
    // else: leave unmarked — released unchanged at commit
}

var summary = await lease.CommitAsync();
// summary.Updated / Deleted / ReleasedUnchanged / Failures
```

Acquisition is sequential, ordered by key — so two leases targeting overlapping sets always acquire in the same order. No AB / BA deadlocks. If any acquisition fails, partial locks are rolled back.

Commit is sequential by default: each marked decision is applied in order, and per-decision failures are collected into `summary.Failures` rather than thrown. For all-or-nothing semantics, pass `transactional: true`:

```csharp
var summary = await lease.CommitAsync(transactional: true);
// All decisions land or none do. A single failed decision aborts the transaction.
```

Transactional commit requires a replica set or sharded MongoDB cluster (see [Transactions](transactions.md)).

`LockManyAsync` also accepts a `FilterDefinition<TEntity>` or an `Expression<Func<TEntity, bool>>` — both resolve to an id list at acquire time.

## See also

- [Transactions](transactions.md) — combining locks with multi-collection atomicity
- [API: ILockableRepositoryCollection&lt;TEntity, TKey&gt;](xref:Tharga.MongoDB.Lockable.ILockableRepositoryCollection`2)
- [API: DocumentLease&lt;TEntity&gt;](xref:Tharga.MongoDB.Lockable.DocumentLease`1)
