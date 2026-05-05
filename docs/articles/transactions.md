# Transactions

Multi-document writes can be wrapped in a MongoDB transaction so they all commit atomically (or all roll back on exception). Works across multiple collections in the same cluster, and supports both Disk and Lockable repositories — including taking and committing locks inside the same transaction.

## Basic usage

```csharp
await mongoDbServiceFactory.WithTransactionAsync(async (session, ct) =>
{
    await jobsRepo.AddAsync(job, session);
    await statsRepo.AddAsync(stat, session);

    await using var scope = await accountRepo.LockAsync(accountId, session: session);
    await scope.CommitAsync(CommitMode.Update, scope.Entity with { Balance = newBalance });
});
```

The session is created on the cluster identified by `configurationName` (default if null). The driver retries on transient transaction errors automatically. Body exceptions abort and rethrow.

## The common case — `LockManyAsync` + transactional commit

For the most common pattern — *lock N docs, decide each, commit atomically* — you don't need to touch `IClientSessionHandle` at all:

```csharp
await using var lease = await coll.LockManyAsync(ids);

// inspect, mark each one
foreach (var doc in lease.Documents)
{
    if (ShouldCommit(doc))
        lease.MarkUpdate(doc.Id, doc with { Status = "processed" });
    else
        lease.MarkRelease(doc.Id);
}

var summary = await lease.CommitAsync(transactional: true);
```

Acquisition stays unchanged (sequential, fast); the commit pass runs inside an internal transaction so all marked decisions land atomically. This avoids the 60-second transaction timeout cost during your "thinking" phase between lock and commit.

## Requirements

- **Replica set or sharded cluster.** MongoDB transactions don't work on standalone deployments; the driver throws on `StartTransaction`.
- All collections inside one transaction must be backed by the same `MongoClient` (same cluster). Cross-cluster transactions aren't supported by MongoDB.
- Default 60-second transaction timeout — don't try to work around it. For long workflows, do the deciding outside the transaction and only wrap the commit pass.

## Behaviour under an active session

- **Index DDL is skipped** when a session is active (MongoDB forbids index management inside a transaction). Indexes are assured by the next non-transactional call against the collection. If your transaction is the *first* call against a fresh collection on process startup, the index won't be assured until later — warm up the collection at startup with a cheap read.
- `DropEmptyAsync` (auto-drop after the last delete) is similarly skipped under a session.

## Out of scope

- Cross-cluster transactions
- Nested transactions / savepoints
- Long-running transactions (60-second cap is a MongoDB constraint)
- Session-aware reads (`GetAsync(filter, session)` etc.) — write atomicity is the focus; reads inside the same session are filed as a follow-up

## See also

- [Lockable collections](lockable-collections.md) — the lock-and-decide workflow that pairs with transactional commits
- [API: MongoDbServiceFactoryTransactionExtensions](xref:Tharga.MongoDB.MongoDbServiceFactoryTransactionExtensions)
