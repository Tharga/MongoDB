# Feature: Interception seam on repository operations (pre-call, veto-capable)

**Source:** `Requests.md` → `## Tharga.MongoDB` → "Interception seam on repository operations
(pre-call, veto-capable)", from Tharga.Platform, 2026-07-25, Priority High.
No GitHub issue filed yet.

## Goal

Give consumers a DI-resolved, veto-capable pre-call hook on every repository operation that reaches
the MongoDB driver, so a consumer can guarantee that no database call happens without its own
policy layer having run first.

The package stays **mechanism only** — it knows nothing about teams, authorization or latency. It
resolves whatever interceptors are registered and runs them in order.

## Why the existing hook does not serve

`RepositoryCollectionBase.ActionEvent` (`RepositoryCollectionBase.cs:16`) proves the interception
points exist, but it cannot be used here:

- **Observational** — an `EventHandler` cannot reject a call.
- **Static** — global across DI containers, so no per-container configuration, and state leaks
  between tests in the same process.

`ActionEvent` is unchanged by this feature. It stays as the observational/telemetry channel.

## Design decisions (settled 2026-07-25)

### 1. Veto — result record, throw also honored
`BeforeCallAsync` returns `InterceptDecision`. `InterceptDecision.Reject(reason)` makes the pipeline
throw a package-owned `CollectionAccessDeniedException` carrying the reason and the
`CollectionCallInfo`. An interceptor that throws on its own aborts the call too and its exception
propagates unchanged.

The result record is the documented path (uniform exception type for callers to catch, matches the
repo's "prefer functional patterns" guideline); the throw path exists so an interceptor that already
has a meaningful domain exception is not forced to launder it through a string reason.

### 2. Two timing points — invocation default, enumeration opt-in
`CollectionCallInfo` carries the timing the interceptor asked for. Registration declares it:

- **`InterceptionPoint.Invocation`** (default) — fires when the calling code made the request, while
  its ambient context is still in scope. Correct for an authorization gate, and the only correct
  point for `IAsyncEnumerable` operations whose database work would otherwise happen at enumeration
  time, long after the call site.
- **`InterceptionPoint.Enumeration`** — fires inside the iterator, at the point the driver work
  actually happens. Needed by anything that wants to affect the observed latency or ordering of a
  deferred result.

An interceptor may declare both. The pipeline never hard-codes one point.

### 3. Lockable reports disk-level operations
`LockableRepositoryCollectionBase` delegates every operation to an inner `Disk` collection
(`Lockable/LockableRepositoryCollectionBase.cs:32` and ~50 `Disk.*` call sites), including lock
acquire, commit, release and extend. Interception therefore sits entirely in
`DiskRepositoryCollectionBase` and lockable collections are covered with **zero new call sites**.

Consequence, accepted deliberately: an interceptor sees `PickForUpdateAsync` as `UpdateOneAsync` —
the disk operation that actually ran. Sufficient for an access gate, which cares that a call
happened and against what collection, not which semantic wrapper made it. Semantic lockable
operation names are recorded as a follow-up, not built here.

### 4. Rejected calls are invisible to the monitor
The interceptor chain runs **before** `FireCallStartEvent`, so a rejected call never enters the
monitor, the call history or `/developer/database`. Rationale: a rejected call never touched the
database, so recording it as a database call would misreport what the database did. Consumers that
need an audit trail of rejections have the natural place for it — the interceptor itself, which is
their own code and already holds the reason.

## Scope

### In
- `ICollectionInterceptor` + `CollectionCallInfo` + `InterceptDecision` + `InterceptionPoint` +
  `CollectionAccessDeniedException` — public contract in `Tharga.MongoDB`.
- Registration on `DatabaseOptions` (`o.AddCollectionInterceptor<T>()`), resolved from DI, running
  in registration order.
- Interceptors reach collections via `IMongoDbServiceFactory` — the one dependency every
  `RepositoryCollectionBase` constructor takes (`RepositoryCollectionBase.cs:38`), so both
  acquisition routes are covered: `ICollectionProvider` (built via `Activator.CreateInstance` in
  `Internals/CollectionProvider.cs`) **and** direct construction with the factory, which is what
  Platform's `TeamRepositoryCollection`, `UserRepositoryCollection`, `IconRepositoryCollection` and
  `ApiKeyRepositoryCollection` all do.
- Both chokepoints in `DiskRepositoryCollectionBase`:
  - `ExecuteAsync(functionName, action, operation, …)` — `:59`, all `Task`-returning operations.
  - `StreamCursorAsync(functionName, queryFactory, …)` — `:1094`, all `IAsyncEnumerable` operations.
- `DropCollectionAsync` (`:1282`), which today bypasses both chokepoints and calls
  `_mongoDbService.DropCollectionAsync` directly.
- Zero-cost fast path when no interceptor is registered.
- Docs: README section + `docs/articles/` page.

### Out
- **No reference interceptor ships in this feature.** The latency simulator that Platform suggests
  bundling is deliberately deferred — the seam is what unblocks them, and a dev-only feature should
  not gate a High-priority authorization seam. Recorded as a follow-up in `planned/README.md`.
- No change to `ActionEvent`, and no deprecation of it.
- No semantic lockable operation names (follow-up).
- No post-call or on-exception hook. Pre-call only, as requested.

## Acceptance criteria

1. `ICollectionInterceptor` registered via `o.AddCollectionInterceptor<T>()` fires before **every**
   operation that reaches the driver, on both `DiskRepositoryCollectionBase` and
   `LockableRepositoryCollectionBase`, via both acquisition routes.
2. `InterceptDecision.Reject(reason)` prevents the operation from executing and surfaces
   `CollectionAccessDeniedException` with the reason and `CollectionCallInfo` intact.
3. An interceptor that throws prevents the operation and its exception propagates unchanged.
4. Multiple interceptors run in registration order; the first rejection short-circuits the rest.
5. `InterceptionPoint.Invocation` on an `IAsyncEnumerable` operation fires when the method is
   called, **not** when the result is enumerated — pinned by a test that calls without enumerating.
6. `InterceptionPoint.Enumeration` fires inside the iterator.
7. With no interceptors registered, neither chokepoint allocates or makes a virtual dispatch on the
   interception path — pinned by a test asserting the fast path.
8. Interceptor state does not leak between DI containers — pinned by a test building two containers
   with different interceptors in one process.
9. Full test suite passes (excluding the known-environmental replica-set tests).
10. README and `docs/articles/` updated.

## Done condition

All acceptance criteria met, packages current, docs landed, `Requests.md` entry marked Done with a
`## Follow-up` line telling Tharga.Platform which version to pick up.

## Version impact

**No breaking change.** Purely additive to the public surface: new types, one new `DatabaseOptions`
method. No existing public signature changes, and `IMongoDbServiceFactory` is deliberately left
untouched (adding to a public interface would break consumers implementing it).

**No behaviour change for existing consumers.** With no interceptor registered the chokepoints take
a field-read fast path and do nothing. The iterator-deferral rework (see plan Step 4) restructures
several streaming methods into wrapper + iterator pairs, but with zero interceptors the observable
behaviour is identical. Consumers who *do* register an interceptor opt into one intentional change:
a streaming call fires interceptors when called rather than when enumerated.

**Bump to 2.14.** Version is CI-computed — `MAJOR_MINOR: '2.13'` at `.github/workflows/build.yml:10`
with the patch auto-incremented from git tags, so an untouched merge would ship 2.13.1. Editing that
line to `2.14` makes the next master build 2.14.0. Justified by repo precedent rather than by a
break: 2.11.0 (`ExtendLockAsync`, explicitly "additive, opt-in, no breaking change") and 2.13.0
(public `Lock` ctor + `WithLock`) were both minor bumps for additive public API. A new public
extension point belongs in that category.
