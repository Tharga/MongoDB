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

## Step 2 — Public contract `[x] done`

Done 2026-07-25. All types under `Tharga.MongoDB/Interception/`, namespace
`Tharga.MongoDB.Interception` — matching the repo's area-folder convention (`Paging`, `Lockable`,
`Disk`). Builds clean on all three TFMs (net8.0/9.0/10.0), 0 warnings. 8 new tests, full suite
643 passed / 5 environmental failures / 8 skipped.

- [x] `ICollectionInterceptor` — `ValueTask<InterceptDecision> BeforeCallAsync(CollectionCallInfo call, CancellationToken cancellationToken = default)`.
- [x] `CollectionCallInfo` — record carrying `CollectionName`, `Operation` (the `functionName`
      already passed to the chokepoints), `OperationType` (the existing `Operation` enum:
      Read/Create/Update/Delete), `EntityType`, `ConfigurationName`, `DatabaseName`,
      `DatabaseContext`, and `Point` (which timing point this invocation represents).
- [x] `InterceptDecision` — `readonly record struct`, `Proceed` / `Reject(string reason)`. Struct
      chosen so the common Proceed path allocates nothing on the hot path; `default` is `Proceed`,
      pinned by a test so a never-set decision can never silently block a call.
- [x] `InterceptionPoint` — `[Flags]` enum `Invocation` / `Enumeration`. No `None` member: a
      zero value would let an interceptor register and silently never fire.
- [x] `CollectionAccessDeniedException` — carries `Reason` and `CollectionCallInfo`.
- [x] XML docs on all public members.
- [x] Tests: `Interception/InterceptionContractTests.cs`, 8 shape tests.

### Two deviations from the step as originally written

1. **`BeforeCallAsync` gained a `CancellationToken`** (defaulted, so implementers may ignore it).
   Not in Platform's sketch. Added now because adding it later would be a breaking change to a
   public interface, and an interceptor doing async policy work (cache or remote lookup) needs it.
   Trivial to revert while unreleased.
2. **Timing points are declared on the interceptor, not at registration.** `ICollectionInterceptor`
   has `InterceptionPoint Points => InterceptionPoint.Invocation` as a *default interface member*,
   so a policy gate implements only `BeforeCallAsync` and never thinks about timing. This satisfies
   Platform's design point #1 ("let an interceptor declare which point it wants") more directly than
   registration-site declaration, and keeps `AddCollectionInterceptor<T>()` a one-argument call.
   Verified default interface members compile on net8.0.

## Step 3 — Registration and resolution `[x] done`

Done 2026-07-25. 10 new tests in `Interception/InterceptorRegistrationTests.cs`; suite at
653 passed / 5 environmental / 8 skipped, stable across two consecutive full runs.

- [x] `DatabaseOptions.AddCollectionInterceptor<T>()` where `T : ICollectionInterceptor`, plus an
      instance overload; preserves registration order.
- [x] Register the interceptor types in DI in `AddMongoDB` via `TryAddSingleton`, so a consumer that
      pre-registered an interceptor with its own dependencies keeps their registration (pinned by
      `ConsumerRegisteredType_IsNotOverwritten`).
- [x] Resolve the ordered interceptor chain into `MongoDbServiceFactory` at construction — same
      pattern as `CommandMonitor` / `RecordingState`.
- [x] Expose the chain as an **internal member on the concrete `MongoDbServiceFactory` class**, not
      on `IMongoDbServiceFactory`. The interface is public, so adding a member to it would break any
      consumer implementing it (test doubles). Follow the established pattern: `CommandMonitor`
      (`Internals/MongoDbServiceFactory.cs:40`) and `RecordingState` (`:43`) are internal members on
      the concrete class, read via `((MongoDbServiceFactory)_mongoDbServiceFactory).CommandMonitor`
      (`Disk/DiskRepositoryCollectionBase.cs:78`). **`IMongoDbServiceFactory` is not touched.**
      The factory is the single dependency both acquisition routes share, which is what makes this
      the coverage-critical decision.
- [x] Precompute fast-path flags on the factory so the chokepoints branch on a field read
      (acceptance criterion 7). Split into **two** flags rather than one — `HasInvocationInterceptors`
      and `HasEnumerationInterceptors` — so Step 5 can skip the enumeration pass entirely when no
      registered interceptor asked for that point.
- [x] Tests: two DI containers in one process register different interceptors and do not see each
      other's (acceptance criterion 8) — this is the concrete thing static `ActionEvent` gets wrong.
      Worth noting the precedent sits three lines away in the same method:
      `MongoDbRegistrationExtensions.cs:72` does `RepositoryCollectionBase.ActionEvent += …`, which
      accumulates a handler on every `AddMongoDB` call in the process and writes a static
      `_actionEvent` field that the last call wins. The new chain must never behave that way.

### Unplanned: fixed a latent race in an unrelated test

`RevalidationQueueTests.HighPriorityKeys_DrainBeforeLow` failed once the 10 new registration tests
were added — each builds a full DI container, and that thread-pool load exposed a pre-existing race.
Verified it was a trigger and not a cause: the suite is green with the new tests filtered out, and
the test passes in isolation. The previous feature's notes already list it as "one rotating
timing-flaky test".

Root cause was in the test, not in `RevalidationQueue`: the drain loop started in the constructor
(`RevalidationQueue.cs:34`) and every `Enqueue` signals it, so under load both low keys could be
dequeued before the high keys were enqueued. That is correct queue behaviour — priority only decides
between items pending *at the same time* — but it made the assertion non-deterministic. Blocking the
refresh callback does not fix it either: with `maxConcurrent: 1` the loop dequeues one more item
before parking on the gate.

Fix (user-approved, touches one unrelated production file): an internal deferred-start overload plus
an idempotent `Start()` on `RevalidationQueue`, used by the test to populate both queues before the
loop runs. The public constructor is unchanged and still starts eagerly. The assertion is now the
stronger, exact `high-1, high-2, low-1, low-2`. Stable across two consecutive full-suite runs.

## Step 4 — Fire at the invocation chokepoints `[x] done`

Done 2026-07-25. 14 new tests in `Interception/InterceptorPipelineTests.cs`; suite at
667 passed / 5 environmental / 8 skipped. No regressions despite restructuring five heavily-exercised
streaming methods.

- [x] `DiskRepositoryCollectionBase.ExecuteAsync` — run the chain *before*
      `FireCallStartEvent`, so a rejected call never enters the monitor as a started call. Placed
      before the `try`, so the `finally` that raises `OnCallEnd` does not run either. Pinned by
      `RejectedCall_IsInvisibleToTheMonitor`.
- [x] **Iterator-deferral rework — the subtlest part of the feature.** Any `async IAsyncEnumerable`
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

      **As built.** Rather than 6 near-identical wrappers, interception happens at *public entry
      points only*, never in the shared internals — otherwise `GetAsync` would intercept once at
      invocation and again at enumeration when its iterator reached `StreamCursorAsync`. Concretely:
      - `GetAsync(filter)`, `GetProjectionAsync(filter)`, `GetDirtyAsync()`, `ExecuteManyAsync()`
        each became a non-iterator wrapper over a new private `…IteratorAsync` method.
      - `GetAsync(predicate)` and `GetProjectionAsync(predicate)` just delegate to their filter
        overloads (now non-iterators), so they intercept exactly once with no wrapper of their own.
      - `GetDirtyIteratorAsync` calls `GetIteratorAsync` directly rather than public `GetAsync`, so
        the scan reports once as `GetDirtyAsync` instead of a nested `GetAsync`.
      - `StreamCursorAsync` does **not** intercept at invocation. It stays the shared internal and
        will carry only the Enumeration point in Step 5.
      - Lockable `GetUnlockedAsync(predicate)` turned out to be a pure pass-through, so it became a
        direct `return Disk.GetAsync(...)` — no wrapper, no iterator, deferral gone.
- [x] Helper shape: `BeginInvocationInterception` returns `ValueTask?` — null when nothing is
      registered *or* the chain finished synchronously (having already thrown on rejection at the
      call site); non-null only when an interceptor genuinely yielded, in which case
      `PrefixAwaitAsync` awaits it before the stream produces anything. `ValueTask?` is a struct, and
      the design avoids lambdas entirely so the fast path allocates no closure.
- [x] `PrefixAwaitAsync` carries `[EnumeratorCancellation]` and forwards the token via
      `.WithCancellation(...)`, so `foreach (… .WithCancellation(ct))` behaves identically whether or
      not interceptors are registered. Without this the token was silently dropped on the
      interceptor path.
- [x] Acceptance criterion 5 pinned by three tests that call `GetAsync` / `GetProjectionAsync` /
      `GetDirtyAsync` and assert interception **without enumerating**.
- [x] `DropCollectionAsync` — bypassed both chokepoints; chain wired in explicitly.
- [x] Tests: representative op per family, rejection prevents execution, interceptor throw
      propagates unchanged, order and short-circuit, enumeration-only interceptor stays silent at
      invocation, async interceptor still gates the operation, and no-interceptor behaviour is
      unchanged.

### Rejection timing, as built

A **synchronous** interceptor (the expected shape, and what an `AsyncLocal` policy check is) rejects
at the *call site* even for streaming operations — `Collection.GetAsync(…)` throws before returning
the stream. An interceptor that genuinely yields cannot complete synchronously, so its rejection
surfaces on first enumeration instead. Either way the operation never reaches the driver. Both paths
are pinned by tests.

## Step 5 — Enumeration timing point `[~] next`

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

Step 1 done — packages current, version line at 2.14, tests at baseline.

Step 2 done — public contract under `Tharga.MongoDB/Interception/`, 8 shape tests, suite at
643 passed / 5 environmental / 8 skipped. Two deviations recorded in the step above (added
`CancellationToken`; timing points declared via a default interface member rather than at
registration) — both still cheap to reverse since nothing is released.

Step 3 done — `AddCollectionInterceptor<T>()` + instance overload, `TryAddSingleton` DI wiring, the
ordered chain resolved onto the concrete `MongoDbServiceFactory`, and two precomputed fast-path
flags. Container isolation is pinned by test. Also fixed a latent race in the unrelated
`RevalidationQueueTests.HighPriorityKeys_DrainBeforeLow` that the new tests exposed (see Step 3).

Suite: 653 passed / 5 environmental / 8 skipped, stable across two consecutive runs.

Step 4 done — the seam is live. Chain fires before `FireCallStartEvent` (rejections invisible to the
monitor), the iterator-deferral rework landed across all public streaming entry points, and
`DropCollectionAsync`'s hole is closed. 14 tests. Suite at 667 passed / 5 environmental / 8 skipped.

**Next: Step 5 — enumeration timing point.** Fire `InterceptionPoint.Enumeration` inside
`StreamCursorAsync` around `OpenCursorWithinLimiterAsync`, gated on the already-precomputed
`HasEnumerationInterceptors` flag so nothing is paid when unused. One decision to settle and record:
cursor-open only, or per `MoveNextWithinLimiterAsync` batch as well — leaning cursor-open only, since
per-batch is a hot inner loop and nothing in the request needs it.
