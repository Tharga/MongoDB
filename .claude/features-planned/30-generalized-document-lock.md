# Feature: Generalised document lock with commit-time update/delete decision

## Source
Requested from the Berakna project (consumer) in April 2026. Today the library offers `LockForUpdate` and `LockForDelete` — two separate APIs that force the caller to decide *before* locking whether the final outcome will be an update or a delete. Consumers sometimes need to inspect multiple locked documents first and then decide per-document whether each should be updated, deleted, or left alone — and commit all of those decisions atomically.

Depends on feature #29 (transactions).

## Goal
Introduce a generalised lock primitive that:
1. Locks one or more documents (possibly across collections).
2. Lets the caller inspect the locked data.
3. On release/commit, the caller chooses per-document whether to **update**, **delete**, or **release unchanged** — all within a single transaction.

## Scope

### High-level shape
- A new `LockAsync` (or similar) that acquires locks on one or more documents without pre-committing to update-vs-delete.
- The returned handle exposes the locked documents and accepts per-document commit decisions.
- On commit, all staged decisions execute inside a transaction (feature #29).
- On disposal without explicit commit, the lock is released without changes.

### Example usage (illustrative)
```csharp
await using var lease = await _collection.LockAsync([id1, id2, id3]);

foreach (var doc in lease.Documents)
{
    if (ShouldDelete(doc))
        lease.MarkForDelete(doc.Id);
    else if (HasChanges(doc))
        lease.MarkForUpdate(doc with { ... });
    // else: release unchanged
}

await lease.CommitAsync(); // All decisions apply atomically
```

### Requirements
- Builds on feature #29 — the commit executes inside a transaction.
- Supports locking across multiple collections in the same lease.
- Handles lock timeouts / expiry consistently with existing lock primitives.
- Failure to commit (transaction aborts) leaves the documents in their original state and releases the locks.

## Out of scope
- Distributed locks across different MongoDB clusters.
- Read-only locks (that's just a read with snapshot isolation — use a transaction).

## Acceptance Criteria
- [ ] `LockAsync` API locks one or more documents without pre-committing to outcome
- [ ] Per-document commit decisions: `MarkForUpdate`, `MarkForDelete`, release-unchanged (default)
- [ ] Commit applies all decisions atomically via transactions (feature #29)
- [ ] Disposal without commit releases locks cleanly
- [ ] Tests cover: mixed update/delete/unchanged commit, commit failure, disposal without commit, lock expiry mid-lease
- [ ] README documents the primitive with an example

## Done Condition
Consumers can lock multiple documents, decide per-document at commit time whether each should be updated, deleted, or released unchanged, and apply all those decisions atomically.
