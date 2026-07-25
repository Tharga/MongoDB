# Plan: Interception seam on repository operations

Branch: `feature/collection-interceptor` (from `master`, in sync with origin at start).
Feature scope: see `plan/feature.md`.

## Step 1 — Package upgrades (mandatory, before any feature code) `[x] done`

Done 2026-07-25. Baseline captured before upgrading (635 passed / 5 failed / 8 skipped, 648 total);
identical after, so the upgrades — including the `MongoDB.Driver` minor — caused no regression. The
5 failures are the known-environmental `TransactionsTests` ("Standalone servers do not support
transactions" — needs a replica set), matching the documented baseline from the previous feature.
Commits: `e248583` (packages), `e965e8f` (version line).

- [x] Apply all available updates across the whole solution (`dotnet outdated -u`). From the
      start-of-feature scan, all are patch/minor — no majors:
      - `MongoDB.Driver` 3.9.0 → 3.10.0 (minor, core dependency — the one to watch)
      - `Microsoft.Extensions.*` 10.0.9 → 10.0.10 (Configuration.Binder, DI.Abstractions,
        Diagnostics.HealthChecks, Features, Hosting.Abstractions, Http)
      - `Microsoft.SourceLink.GitHub` 10.0.300 → 10.0.301
      - `Tharga.Blazor` 2.2.1 → 2.2.2
      - `Microsoft.AspNetCore.Mvc.Testing` 10.0.9 → 10.0.10,
        `Microsoft.NET.Test.Sdk` 18.7.0 → 18.8.1
      - Sample/template: `Microsoft.AspNetCore.Components.WebAssembly[.Server]` 10.0.9 → 10.0.10
- [x] `dotnet build -c Release` clean (0 warnings), `dotnet test -c Release` at baseline.
- [x] Commit `chore: update nuget packages`.
- [x] Bump `MAJOR_MINOR: '2.13'` → `'2.14'` at `.github/workflows/build.yml:10`. Confirmed it is a
      single workflow-level `env` entry inherited by all three jobs — not three separate literals —
      so one edit covers build, release and docs. PR builds now stamp `2.14.0-pre.N`.

## Step 2 — Public contract `[~] next`

- [ ] `ICollectionInterceptor` — `ValueTask<InterceptDecision> BeforeCallAsync(CollectionCallInfo call)`.
- [ ] `CollectionCallInfo` — record carrying `CollectionName`, `Operation` (the `functionName`
      already passed to the chokepoints), `OperationType` (the existing `Operation` enum:
      Read/Create/Update/Delete), `EntityType`, `ConfigurationName`, `DatabaseName`,
      `DatabaseContext`, and `Point` (which timing point this invocation represents).
- [ ] `InterceptDecision` — `Proceed` / `Reject(string reason)`, `init`-only, readonly struct or
      record per the repo's functional-pattern guideline.
- [ ] `InterceptionPoint` — `[Flags]` enum `Invocation` / `Enumeration`.
- [ ] `CollectionAccessDeniedException` — carries `Reason` and `CollectionCallInfo`.
- [ ] XML docs on all public members (required by shared-instructions coding guidelines).
- [ ] Tests: contract-shape tests only at this step (construction, `Reject` carries reason).

## Step 3 — Registration and resolution

- [ ] `DatabaseOptions.AddCollectionInterceptor<T>()` where `T : ICollectionInterceptor`, plus an
      instance overload; preserves registration order.
- [ ] Register the interceptor types in DI in `AddMongoDB` alongside the existing service
      registrations (`MongoDbRegistrationExtensions.cs`).
- [ ] Resolve the ordered interceptor chain into `MongoDbServiceFactory` at construction
      (`MongoDbRegistrationExtensions.cs:110-126` is where the factory is built and configured —
      same pattern as `CommandMonitor` / `RecordingState`).
- [ ] Expose the chain as an **internal member on the concrete `MongoDbServiceFactory` class**, not
      on `IMongoDbServiceFactory`. The interface is public, so adding a member to it would break any
      consumer implementing it (test doubles). Follow the established pattern: `CommandMonitor`
      (`Internals/MongoDbServiceFactory.cs:40`) and `RecordingState` (`:43`) are internal members on
      the concrete class, read via `((MongoDbServiceFactory)_mongoDbServiceFactory).CommandMonitor`
      (`Disk/DiskRepositoryCollectionBase.cs:78`). **`IMongoDbServiceFactory` is not touched.**
      The factory is the single dependency both acquisition routes share, which is what makes this
      the coverage-critical decision.
- [ ] Precompute an `bool HasInterceptors` flag on the factory so the chokepoints can branch on a
      field read (acceptance criterion 7).
- [ ] Tests: two DI containers in one process register different interceptors and do not see each
      other's (acceptance criterion 8) — this is the concrete thing static `ActionEvent` gets wrong.

## Step 4 — Fire at the invocation chokepoints

- [ ] `DiskRepositoryCollectionBase.ExecuteAsync` (`:59`) — run the chain *before*
      `FireCallStartEvent`, so a rejected call never enters the monitor as a started call.
- [ ] **Iterator-deferral rework — the subtlest part of the feature.** Any `async IAsyncEnumerable`
      method defers its whole body until the first `MoveNextAsync`. Firing the chain from inside such
      a method would fire at *enumeration*, which is exactly the failure Platform's design point #1
      warns about. Every public streaming entry point that is an iterator must become a thin
      non-iterator wrapper that runs the chain eagerly and returns the iterator. Full list:
      - `Disk/DiskRepositoryCollectionBase.cs:273` — `GetAsync(predicate, …)`
      - `Disk/DiskRepositoryCollectionBase.cs:285` — `GetAsync(filter, …)`
      - `Disk/DiskRepositoryCollectionBase.cs:321` — `GetProjectionAsync<T>(filter, …)`
      - `Disk/DiskRepositoryCollectionBase.cs:1094` — `StreamCursorAsync` itself
      - `Disk/DiskRepositoryCollectionBase.cs:1291` — `GetDirtyAsync()`
      - `Lockable/LockableRepositoryCollectionBase.cs:380` — `GetUnlockedAsync(predicate, …)`

      Already non-iterators, no change needed: `GetProjectionAsync(predicate)` (`:312`),
      `ExecuteManyAsync` (`:1089`), and the lockable methods that return `Disk.GetAsync(...)`
      directly (`:388`, `:638`, `:644`, `:681` — the trailing `.Select(...)` is lazy but the
      `Disk.GetAsync` *call* is eager, which is what matters).

      **This is the one place decision #3 ("zero new call sites in lockable") does not fully hold** —
      `GetUnlockedAsync(predicate)` needs the wrapper too. One method, not fifty.
- [ ] Acceptance criterion 5 pins this: call each streaming entry point **without** enumerating and
      assert the interceptor fired.
- [ ] `DropCollectionAsync` (`:1282`) — currently bypasses both chokepoints; run the chain here too.
- [ ] Tests: interception fires for a representative op of each family (`CountAsync` via
      `ExecuteAsync`, `GetAsync` via `StreamCursorAsync`, `DropCollectionAsync`); rejection prevents
      execution; interceptor throw propagates unchanged; order and short-circuit.
- [ ] Test: calling `GetAsync(...)` **without** enumerating still fires an `Invocation` interceptor.

## Step 5 — Enumeration timing point

- [ ] Fire `InterceptionPoint.Enumeration` inside the iterator, at the point the driver work
      happens — around `OpenCursorWithinLimiterAsync` (`:1187`).
- [ ] Decide and document whether it also fires per `MoveNextWithinLimiterAsync` batch (`:1255`) or
      only on cursor open. Default: **cursor open only** — per-batch is a hot inner loop and nothing
      in the request needs it. Record the decision in `feature.md` when settled.
- [ ] Skip the enumeration pass entirely when no registered interceptor declared that point.
- [ ] Tests: enumeration-point interceptor fires only on enumeration; an interceptor declaring both
      points sees both, with `CollectionCallInfo.Point` distinguishing them.

## Step 6 — Lockable coverage verification

- [ ] No production code expected here — `LockableRepositoryCollectionBase` delegates through
      `Disk`. This step is **verification**, and exists because "partial coverage reads as protection
      while leaving holes" is the explicit risk in the request.
- [ ] Audit every `Disk.*` call site in `Lockable/LockableRepositoryCollectionBase.cs` and confirm
      each lands on an intercepted path. Known sites to confirm: `AcquireLockAsync` (`:818`),
      `ExtendLockCoreAsync` (`:626`), `ReleaseOneAsync` (`:703`), `ReleaseManyAsync` (`:711`),
      `ReleaseAsync` (`:961`), `PrepareCommitForUpdateAsync` (`:1004`),
      `PerformCommitForDeleteAsync` (`:1013`, `:1016`).
- [ ] Also audit `Lockable/DocumentLease.cs`, `EntityScope.cs`, `LockScope.cs` — the earlier grep
      found no direct driver access in them; confirm that holds.
- [ ] Tests: a lockable pick/commit cycle fires interceptors for the underlying disk operations.

## Step 7 — Fast-path cost

- [ ] Confirm the no-interceptor path is a field read and a branch — no allocation, no virtual
      dispatch, no `CollectionCallInfo` construction (it must be built lazily, only once at least
      one interceptor is registered).
- [ ] Test pinning acceptance criterion 7.

## Step 8 — Documentation

- [ ] `README.md` — new section on the interception seam: contract, registration, the two timing
      points, the reject/throw semantics, and an explicit note that it is mechanism-only.
- [ ] `docs/articles/` — new `collection-interceptors.md` following the existing one-file-per-area
      pattern (`lockable-collections.md`, `transactions.md`, `monitoring.md`); add to
      `docs/articles/toc.yml`.
- [ ] Document the relationship to `ActionEvent` so the two hooks are not confused: `ActionEvent` is
      static + observational (telemetry), `ICollectionInterceptor` is DI-scoped + veto-capable
      (policy).
- [ ] Land as a separate `docs:` commit.

## Step 9 — Close-out (only on user confirmation)

- [ ] Re-run `dotnet outdated`; apply anything new in this PR.
- [ ] Full test suite green.
- [ ] Mark the `Requests.md` entry Done with date + summary; add the `## Follow-up` line for
      Tharga.Platform naming the version.
- [ ] Record follow-ups in `planned/README.md`: (a) reference latency-simulator interceptor,
      (b) semantic lockable operation names, (c) post-call / on-exception hook if a consumer asks.
- [ ] Archive `plan/feature.md` → `$DOC_ROOT/Tharga/plans/Toolkit/MongoDB/done/collection-interceptor.md`.
- [ ] `git rm -r plan`, final commit `feat: collection-interceptor complete`, push, open PR.

## Open questions

- **`ICollectionInterceptor` lifetime.** Registered types resolve from the root provider at factory
  construction, which makes them effectively singletons. That is right for the intended use — the
  ambient context lives in `AsyncLocal`, not in the interceptor — and it matches the precedent in
  this package (`FlexibleGuidSerializer` uses `AsyncLocal<string>` for per-flow collection context,
  `FlexibleGuidSerializer.cs:16`). Confirm no consumer needs a scoped interceptor before locking it
  in.

## Last session

2026-07-25 — Feature planned and Step 1 completed.

Traced the code: two chokepoints in `DiskRepositoryCollectionBase` cover everything including
lockable (which delegates all ~50 operations to an inner `Disk`); the concrete `MongoDbServiceFactory`
is the one dependency both acquisition routes share, so it carries the interceptor chain;
`DropCollectionAsync` is the one operation that bypasses both chokepoints. Also found the
iterator-deferral problem is broader than `StreamCursorAsync` — six streaming methods need the
wrapper split (Step 4), one of them in lockable.

Four design decisions settled with the user and recorded in `feature.md`: veto style (result record,
throw also honored), timing points (invocation default + enumeration opt-in), granularity
(disk-level operation names), and rejected calls invisible to the monitor.

Step 1 done — packages current, version line at 2.14, tests at baseline, two commits on the branch.

**Next: Step 2 — public contract** (`ICollectionInterceptor`, `CollectionCallInfo`,
`InterceptDecision`, `InterceptionPoint`, `CollectionAccessDeniedException`). No production
behaviour changes in that step; it is types + XML docs + shape tests.
