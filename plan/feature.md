# Feature: sort/ordering control when acquiring a lock

Closes GitHub [#135](https://github.com/Tharga/MongoDB/issues/135).

## Goal

Let a caller decide *which* document is locked when several match, instead of getting an
arbitrary one. The driving scenario is a work queue: many lockable job documents, and a
background engine that must process them in a deterministic order (e.g. lowest `Year`
first, then a `Type` priority).

## Background

`PickForUpdateAsync` / `PickForDeleteAsync` / `LockAsync` all funnel into
`AcquireLockAsync`, which calls
`Disk.UpdateOneAsync(matchFilter, update, OneOption<TEntity>.FirstOrDefault, session)`.

The storage layer already honours a sort on this path — `DiskRepositoryCollectionBase`
assigns `Sort = options.Sort` onto `FindOneAndUpdateOptions`. The only reason ordering is
lost is that the static `OneOption<TEntity>.FirstOrDefault` has `Sort = null`. So this is
an API-surface feature, not a storage-engine feature.

## Scope

A new `PickOptions<TEntity>` record carrying `Sort`, threaded through the lock-acquire
path, exposed as additive overloads on the six filter/predicate entry points:

- `PickForUpdateAsync(filter, ...)` / `PickForUpdateAsync(predicate, ...)`
- `PickForDeleteAsync(filter, ...)` / `PickForDeleteAsync(predicate, ...)`
- `LockAsync(filter, ...)` / `LockAsync(predicate, ...)`

`LockAsync` is included beyond the letter of #135: it shares `AcquireLockAsync` and has
the identical arbitrary-match behaviour, so leaving it out would be an inconsistency in
the same API family.

An options record rather than a plain `SortDefinition<TEntity>` parameter because #136
(per-group exclusivity) is likely to want another pick-time knob, which then becomes a
new property instead of another wave of overloads.

## Out of scope

- The `TKey id` overloads — a single document has nothing to order.
- `LockManyAsync` — acquisition is deliberately ordered by key to avoid AB/BA deadlocks;
  a caller-supplied sort would undermine that.
- `OneOption.Mode` is deliberately **not** exposed. Lock acquisition must stay
  `FirstOrDefault` for atomicity; letting a caller pass `Single` would break it.
- Issue #136 (per-group exclusivity) — discussed separately.

## Acceptance criteria

- [ ] `PickOptions<TEntity>` exists with an `init`-only `Sort`, XML-documented.
- [ ] All six entry points have a sorted overload on both the interface and the base class.
- [ ] Every existing signature is untouched — no positional-argument breakage for consumers.
- [ ] A sorted pick returns the correct document when several match, for ascending and
      descending, for both `PickForUpdate` and `PickForDelete`.
- [ ] Repeated sorted picks drain the queue in order (the actual work-queue scenario).
- [ ] A sorted pick still skips locked documents and honours expired locks.
- [ ] Passing `null` options, or options with a `null` `Sort`, behaves exactly as the
      unsorted overload.
- [ ] Full test suite passes (excluding the 5 pre-existing transaction failures and the
      known-flaky `GetLockedExpired`, both documented in the backlog as environmental).
- [ ] `README.md` and `docs/articles/lockable-collections.md` document the new overloads.

## Done condition

Feature branch pushed, user has tested, docs updated, `plan/` removed in the close-out
commit, PR opened against `master`.
