# Feature: Lockable delayed commit — auto-commit when no other writer has touched the document

## Goal

When a `LockableRepositoryCollection` lease overruns its TTL window but the document hasn't been touched by anyone else, allow `DocumentLease.CommitAsync` to succeed instead of throwing `LockExpiredException`. The pessimistic time-gate today rejects work that's safe to commit; the new behaviour switches that to optimistic-identity (the `LockKey` atomicity check) which already protects against the genuinely-contended case.

## Background

The current commit filter in `LockableRepositoryCollectionBase` gates on:

- `Lock.LockKey == this lease's key` (atomicity — only the original holder can commit).
- `Lock.ExpireTime >= DateTime.UtcNow` (pessimistic TTL — fail if the window passed).

The first condition is what actually keeps concurrent writers from clobbering each other:

- Another worker picks the expired lock via `PickAsync` → writes a new `LockKey` → original holder's commit filter no longer matches → fails.
- Another worker explicitly releases via `ReleaseOneAsync` → `Lock` becomes null → filter no longer matches → fails.
- Nobody else has touched it → original `LockKey` is still there → filter matches even if `ExpireTime` is in the past.

The `ExpireTime` gate is redundant for safety; it only adds a "you took too long" rejection on top. In practice that rejection costs consumers the entire pipeline of work when a single network blip pushes them past TTL.

## Scope

### 1. Default behavioural change

Drop the `Lock.ExpireTime >= now` predicate from the commit filter in all lockable commit paths (`LockAsync` single-document leases, `LockManyAsync` batch leases, transactional and best-effort modes alike). `LockKey == ours` carries the atomicity guarantee; nothing else needs to change for the safety model to hold.

### 2. Opt-out: two layers

**Global default** via `DatabaseOptions.AllowDelayedCommit` (top-level, settable via `appsettings.json` or code), default `true`. Pattern matches the other top-level toggles already on `DatabaseOptions` (`GuidStorageFormat`, `AssureIndex`, `AutoRegisterRepositories`). Nested under a new `LockableOptions` is over-engineered for a single property; extract later if more lockable-only options arrive.

**Per-collection override** via virtual property on `LockableRepositoryCollectionBase<TEntity, TKey>`. Default resolution reads the `DatabaseOptions` value; overriding the property pins the collection regardless of the global setting:

```csharp
public class MyStrictlyTimedCollection : LockableRepositoryCollectionBase<MyEntity>
{
    protected override bool AllowDelayedCommit => false; // always strict, regardless of global
}
```

When `false` (by either route), the `ExpireTime` gate stays in the filter and the commit fails as before.

### 3. Observability

When a commit succeeds *after* the lock's `ExpireTime`, log at **Information** level with a structured `{expiredBy}` field so operators can filter on chronic-TTL-overrun patterns:

```
Lockable entity {entityId} in collection {collection} committed {expiredBy} after lock expiry — no other writer had modified it.
```

Information (not Warning) because the commit succeeded and the system did exactly what the new feature promises. Tight-TTL pattern recognition happens at log-aggregation level, not per event.

### 4. Behaviour under `transactional: true`

The existing `CommitAsync(transactional: true)` path already gives all-or-nothing semantics — if any per-entity filter doesn't match (e.g. another writer took the lock and changed the `LockKey`), the whole transaction rolls back. The new feature works correctly under both modes without special handling — we're only relaxing the *time gate*, the *identity check* drives atomicity unchanged.

## Out of scope

- **Per-entity flag on `DocumentLeaseCommitSummary<TKey>`** indicating which successes were delayed (e.g. `bool CommittedAfterExpiry`). Was on the table, dropped — adds API surface to every success record for a property that's `false` on nearly all of them. Consumers who want metrics can scrape the structured log field. Reconsider if a concrete consumer needs programmatic per-call visibility.
- **A new `LockableErrorKind`** for "would have committed if not for the strict-TTL opt-out". The opt-out path keeps emitting `LockExpiredException` → `LockExpired` exactly as today; no new kind needed.
- **TTL tuning recommendations** in the logs / docs. Different consumers have very different SLA expectations; we surface the `{expiredBy}` field and let them decide.

## Acceptance criteria

- `CommitAsync` succeeds for a lease whose lock has expired, provided no other writer has touched the document (`LockKey` still matches).
- `CommitAsync` still throws `LockExpiredException` (or returns failure in `LockManyAsync`) when another writer has picked the expired lock or explicitly released it.
- `DatabaseOptions.AllowDelayedCommit = false` (set via `appsettings.json` or code) restores strict-TTL behaviour for every lockable collection in the host.
- Collections that override `AllowDelayedCommit => false` retain strict-TTL behaviour regardless of the `DatabaseOptions` value.
- Collections that don't override read the `DatabaseOptions.AllowDelayedCommit` value as their default.
- An `Information` log line fires once per delayed commit with `{entityId}`, `{collection}`, and `{expiredBy}` structured fields.
- Transactional batch commits (`transactional: true`) roll back the whole batch when any single entity's `LockKey` doesn't match — unchanged behaviour, since the new feature relaxes only the time gate.

## Effort

Small to medium. Three production-code touch points (the two commit filter sites + the helper / log), one new property on the summary record, plus tests. ~1 day with tests.

## NuGet

Current as of 2.10.12; no bumps needed.
