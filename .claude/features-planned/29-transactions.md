# Feature: Multi-document transactions

## Source
Requested from the Berakna project (consumer) in April 2026. Consumers currently have no way to make multiple writes atomic across several documents or collections. They also need this primitive as the foundation for a generalised document-lock feature (see feature #30).

## Goal
Introduce a transaction primitive that lets callers execute multiple collection operations (inserts, updates, deletes, upserts — across one or more collections) within a single atomic unit of work, with an explicit commit/rollback at the end.

## Scope

### High-level shape
- A way to open a transaction (from the repository layer or a service).
- Within the transaction, callers can issue multiple writes against multiple collections.
- On success, commit; on exception, the whole unit is rolled back.
- Works on `DiskRepositoryCollectionBase` (MongoDB-backed) and cleanly no-ops or clearly fails on non-transactional backends.
- Respects existing index management and call tracking — transactional operations should show up in the admin UI with transaction context.

### Example usage (illustrative, not a final API)
```csharp
await _repository.WithTransactionAsync(async tx =>
{
    await tx.Collection<JobEntity>().UpsertAsync(job);
    await tx.Collection<JobStatsEntity>().AddAsync(stats);
    await tx.Collection<AuditEntity>().AddAsync(audit);
});
```

### Requirements
- Uses MongoDB server-side sessions (`IClientSessionHandle` + `WithTransactionAsync`) under the hood.
- Works across multiple collections registered in the same MongoDB cluster.
- Must integrate with existing `Operation`/monitoring so transactional calls are visible.
- Must interop with (or be the foundation for) the generalised lock feature.

## Out of scope
- Distributed transactions across different MongoDB clusters.
- Nested transactions / savepoints.
- Long-running transactions (MongoDB has a 60-second default — don't try to work around it).
- Transactional reads with strict isolation levels beyond MongoDB's default snapshot isolation.

## Acceptance Criteria
- [ ] Transaction API exposed on the repository/collection layer
- [ ] Multiple writes across multiple collections commit atomically
- [ ] Exception inside the transaction aborts and rolls back cleanly
- [ ] Admin UI / call tracking shows transactional operations with shared transaction context
- [ ] Tests cover: multi-collection commit, rollback on exception, concurrent conflicting transactions
- [ ] README documents the primitive with an example

## Done Condition
Consumers can execute multiple writes atomically across collections and rely on MongoDB-level transactionality. Feature #30 (generalised lock) can build on this primitive.
