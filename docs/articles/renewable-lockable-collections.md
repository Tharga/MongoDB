# Renewable lockable collections

`IRenewableLockRepositoryCollection<TEntity, TKey>` (namespace `Tharga.MongoDB.Lockable.Renewable`) is a standalone variant of the [lockable collection](lockable-collections.md) built for work whose duration is unpredictable: the lease can be set **short**, and the lock owner **extends it until finished**. A lock always expires after its lease — unless the owner asks for more time.

It is a parallel implementation, not a change to the existing one. `LockableRepositoryCollectionBase` and its scopes are untouched, the on-disk `Lock` subdocument and pickup filters are identical, and both implementations interoperate on the same collections — so a consumer can swap per collection by changing the repository collection's base class, and swap back at any time.

## Why

With a fixed lease you must choose between two failure modes: a lease shorter than the work (the lock expires mid-run, another worker re-picks the document, and the original commit fails with a lock-key mismatch) or a lease long enough for the worst case (a crashed worker blocks the document for the entire lease). Renewal removes the trade-off: a 5-minute lease recovers from a crash within 5 minutes, while a healthy 6-hour job just keeps renewing.

## Swap in

```csharp
// before
internal class MyJobCollection : LockableRepositoryCollectionBase<MyJob, ObjectId> { ... }

// after — same entity, same collection, same data
internal class MyJobCollection : RenewableLockRepositoryCollectionBase<MyJob, ObjectId> { ... }
```

The API mirrors `ILockableRepositoryCollection<TEntity, TKey>` one to one — `PickForUpdateAsync` / `PickForDeleteAsync` / `WaitFor*` / `LockAsync` / `LockManyAsync` / `ReleaseOneAsync` and the rest — returning `RenewableEntityScope` / `RenewableLockScope`, which inherit the ordinary scopes (so `CommitAsync`, `AbandonAsync`, `SetErrorStateAsync`, `ExecuteAsync` and disposal behave exactly the same) and add three members.

## Manual extension

```csharp
await using var scope = await collection.PickForUpdateAsync(id, TimeSpan.FromMinutes(5), actor);

foreach (var item in workItems)
{
    await Process(item);
    await scope.ExtendAsync();      // push ExpireTime forward another lease
}

await scope.CommitAsync(scope.Entity);
```

`ExtendAsync(TimeSpan? extension = null)` atomically moves `Lock.ExpireTime` to `now + extension` (default: the original lease), fenced on the scope's own `LockKey` — it can never extend a lock that another actor has re-picked. It returns the new expiry, and throws:

- `LockLostException` — the lock was stolen (expired and re-picked) or the document was deleted. The scope's `LockLost` token is cancelled **before** the exception propagates.
- `LockExpiredException` — the lease expired and the collection is strict (`AllowDelayedCommit` overridden to `false`). On a default collection an expired-but-not-stolen lock extends successfully — the `LockKey` match, not the wall clock, is the safety authority, consistent with delayed commit.
- `LockAlreadyReleasedException` — the scope was already committed/abandoned.

## Keep-alive

For jobs without natural renewal points, start an automatic renewer:

```csharp
await using var scope = await collection.PickForDeleteAsync(id, TimeSpan.FromMinutes(5), actor);
await using var keepAlive = scope.StartKeepAlive(new LockKeepAliveOptions
{
    MaxTotalDuration = TimeSpan.FromHours(2)    // anti-zombie cap — set this deliberately
});

using var cts = CancellationTokenSource.CreateLinkedTokenSource(scope.LockLost, jobToken);
await RunLongJobAsync(scope.Entity, cts.Token);

await scope.CommitAsync();
```

The loop renews at `Interval` (default: lease ÷ 3) by `Extension` (default: the original lease) and stops automatically when the scope commits, abandons, errors, or disposes — always before the release write. Failure handling:

- Transient errors (network, MongoDB hiccup): logged, `OnRenewalFailure` invoked, retried next tick. If the lease lapses meanwhile and nobody re-picked, the next renewal resurrects it.
- `LockLostException`: the `LockLost` token is cancelled and the loop stops — link the token into the job's `CancellationToken` so the work aborts within roughly one interval instead of running to a doomed commit.
- `MaxTotalDuration` reached: renewals stop with a warning and the lock expires at its last `ExpireTime`. Recovery from that point is exactly the ordinary expired-lock pickup — a hung-but-alive process cannot hold a lock forever.

## Crash recovery and mixed versions

A crashed owner simply stops renewing: the lock expires at most one lease after the last renewal and any worker can pick it up, exactly as with the fixed-lease implementation — just bounded by the short lease instead of a worst-case one.

Because renewal only rewrites `Lock.ExpireTime` inside the existing subdocument, processes running older package versions read and honor the extended expiry through their unchanged filters. Only the lock **owner** needs the new implementation; readers and competing pickers need nothing.

## Concurrency and cost

Renewal is one small `_id`-targeted, `LockKey`-fenced update per held scope per interval — for a 5-minute lease, one tiny write every ~100 seconds per *currently held lock*, independent of collection size. Each scope owns its own renewal state (a per-scope semaphore serializes its extends); there is no cross-scope or cross-collection coordination, no new indexes, and no change to the pickup path. Races resolve by MongoDB document-level atomicity: a renewal landing first makes the stealer's expired-pickup filter miss; a steal landing first makes the renewal's key fence miss and surface as `LockLost`. No interleaving yields two owners.

## Divergence from the fixed-lease implementation

One deliberate difference: the **abandon** path releases the lock fenced on the owner's `LockKey` and treats an already-lost lock as a successful no-op. With renewal, a lease may legitimately expire (e.g. at the `MaxTotalDuration` cap), be re-picked and committed by another actor before the original scope disposes — that disposal must not throw, and must never clear the new owner's lock.

## See also

- [Lockable collections](lockable-collections.md) — the fixed-lease implementation this mirrors
- [API: IRenewableLockRepositoryCollection&lt;TEntity, TKey&gt;](xref:Tharga.MongoDB.Lockable.Renewable.IRenewableLockRepositoryCollection`2)
