# Feature: Fix #133 (monitor NRE) + #132 (lockable Lock construction seam)

Single PR covering two small, independent changes.
Branch: `fix/monitor-nre-and-lockable-seam` → `master`.

---

## Part A — #133: DatabaseMonitor NRE flood with decorated ICollectionProvider

GitHub: https://github.com/Tharga/MongoDB/issues/133

### Root cause
`DatabaseMonitor.GetDynamicRegistrations` does
`_collectionProvider.GetCollection(colType, ctx) as RepositoryCollectionBase`. A consumer that
decorates the provider so `GetCollection<…>()` returns a `DispatchProxy` over the collection
*interface* makes that cast `null`; `collection.BuildIndexMetas()` then dereferences null inside
`IndexMetaConverter.ResolveProperty` → NRE. The throw happens inside `GetLookups()` *before*
`_staticLookup` is assigned/cached, so the build never completes and re-throws on every access —
that is the flood. The static path already null-guards; the dynamic path does not.

### Scope
- Null-guard the dynamic-registration path: not a `RepositoryCollectionBase` → log Debug and skip
  (mirror the static path).
- Defensive null-guard in `IndexMetaConverter.BuildIndexMetas`/`ResolveProperty` (null instance →
  empty, no throw).

### Out of scope (follow-up)
- The issue's "ideal": surface the real collection instance on `CollectionAccessEventArgs` so the
  monitor never re-resolves through a decorated provider (also covers action paths). Larger design
  change — record as a Plan follow-up, do not expand this PR.

### Acceptance
- Decorated provider returning a non-`RepositoryCollectionBase` proxy no longer NREs; that
  collection is skipped; others still monitored.
- `BuildIndexMetas(null)` returns empty, no throw. Unit test(s) cover the guard.

---

## Part B — #132: public seam to construct/attach a Lock (unit-testability)

GitHub: https://github.com/Tharga/MongoDB/issues/132

### Rationale
`Lock` has an `internal` ctor and `LockableEntityBase.Lock` is `internal init`, so consumers can't
build a locked/errored entity in memory to unit-test lock/exception-reading code. Verified the
`internal` ctor guards no correctness invariant: the commit/extend protocol uses the server-issued
lock (LockKey-matched atomic writes) and never trusts a caller-supplied `entity.Lock`; `init`
already gives immutability. `ExceptionInfo` is already public; `EntityScopeBuilder.Build<T>` is
already a public seam for `EntityScope`.

### Scope
- Make `Lock`'s constructor `public` (drop `internal`). `required` still enforces the mandatory
  fields; matches `ExceptionInfo`/`IndexMeta`. No factory method.
- Add public `WithLock` extension on `LockableEntityBaseExtensions` returning `entity with { Lock = @lock }`
  (lives in-assembly, so it can set the `internal init` property). Entity `Lock` property stays
  `internal` — symmetric with the existing `GetLockInfo()` read extension.
- Additive public API → **minor version bump**.

### Acceptance
- Consumer can: `new Lock { LockKey=…, LockTime=…, ExpireTime=…, ExceptionInfo=… }`, attach via
  `entity.WithLock(lock)`, read back via `GetLockInfo()`, and drive `SetErrorStateAsync` through an
  `EntityScopeBuilder.Build(...)` scope — all without a live mongod.
- Unit tests cover construction, attach, read-back, and the error-state path.
- Consumer-facing docs updated (`docs/articles/lockable-collections.md`; README if relevant).

---

## Done condition (both)
- All changes + tests committed on `fix/monitor-nre-and-lockable-seam`; full suite green.
- Close-out commit removes `plan/`.
- One PR → `master`; CI green.
