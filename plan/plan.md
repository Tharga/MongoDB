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

## Step 5 — Enumeration timing point `[x] done`

Done 2026-07-26. 5 new tests; suite at 672 passed / 5 environmental / 8 skipped.

- [x] Fire `InterceptionPoint.Enumeration` inside the iterator. Placed at the **top of
      `StreamCursorAsync`**, which turned out to be the single right spot: it is the only place a
      deferred read reaches the driver, its body does not run until the consumer calls
      `MoveNextAsync`, and all three streaming families (`GetAsync`, `GetProjectionAsync`,
      `ExecuteManyAsync`) funnel through it. One call site, complete coverage.
- [x] **Settled: cursor open only, not per batch.** `MoveNextWithinLimiterAsync` is a hot inner loop
      and nothing in the request needs per-batch granularity. Pinned by
      `EnumerationPoint_FiresOncePerStream_NotPerBatch` — the test collection has `FetchSize` 5 and
      the test streams 12 rows, so the read genuinely spans multiple driver batches while the
      interceptor still sees exactly one call.
- [x] Runs **before** `FireCallStartEvent`, same as the invocation point, so a rejection at either
      point stays invisible to the monitor.
- [x] Skip the enumeration pass entirely when no registered interceptor declared that point — reuses
      the `HasEnumerationInterceptors` flag precomputed in Step 3.
- [x] Tests: fires only on enumeration; an interceptor declaring both points sees both in order with
      `CollectionCallInfo.Point` distinguishing them; does not fire for non-deferred operations
      (`CountAsync`); rejection at the enumeration point prevents the query and reports
      `Point = Enumeration`.

## Step 6 — Lockable coverage verification `[x] done`

Done 2026-07-26. No production code changed — the delegation claim held. 7 new tests in
`Interception/LockableInterceptionCoverageTests.cs`; suite at 679 passed / 5 environmental /
8 skipped.

- [x] Audited every `Disk.*` call site in `Lockable/LockableRepositoryCollectionBase.cs`. All lock
      writes land on the intercepted `ExecuteAsync` chokepoint: `AcquireLockAsync`,
      `ExtendLockCoreAsync`, `ReleaseOneAsync`, `ReleaseManyAsync`, `ReleaseAsync`,
      `PrepareCommitForUpdateAsync`, `PerformCommitForDeleteAsync`. All reads land on intercepted
      public entry points.
- [x] Audited `DocumentLease.cs`, `EntityScope.cs`, `LockScope.cs`. **They hold delegates**
      (`ReleaseAction`, `_releaseAction`, `_extendAction`) handed to them by the collection and never
      touch a driver or collection object themselves, so every write routes back through intercepted
      `Disk.*` calls. This is why commit/release/abandon need no special handling.
- [x] Confirmed the public `ExecuteAsync(Func<IMongoCollection<TEntity>, …>)` escape hatch routes
      through the protected chokepoint, so even raw driver access by a consumer is intercepted.
- [x] Tests: lock acquire + commit, release, extend, pick-for-delete + commit, rejection blocking a
      pick, `GetUnlockedAsync` firing at invocation without enumerating, and a test pinning the
      disk-level naming decision.

### Coverage boundary, stated explicitly

Intercepted: every **public data operation**, on both collection families, via both acquisition
routes. Not intercepted: the `internal` index/clean/admin plumbing — `FetchCollectionAsync`,
`AssureIndex`, `DropIndex`, `CleanAsync`, `CleanCollectionAsync`, `GetCleanInfoAsync`. These do reach
the driver, but they are `internal` (a consumer cannot call them) and are driven by the monitor's own
admin surface, which has its own authorization. Worth documenting in Step 8 so the boundary is
stated rather than discovered.

### Two findings from the audit

1. **Pre-existing bug: `DeleteOneAsync(FilterDefinition, …)` labels itself `nameof(UpdateOneAsync)`**
   (`Disk/DiskRepositoryCollectionBase.cs:1117`). Deletes have always been reported to the monitor
   as "UpdateOneAsync" — this predates the feature. It passes `Operation.Delete` correctly, so
   `CollectionCallInfo.OperationType` is right and an interceptor keying on it behaves correctly;
   only the display string is wrong. **Not fixed here** — correcting it changes what the monitor
   shows for every delete, which is a separate, visible change. Recorded as a follow-up. The test
   asserts on `OperationType` and carries a comment explaining why.
2. **`DropEmptyAsync` calls the public `DropCollectionAsync()`**, so with
   `CreateStrategy.DropEmpty` an emptied collection raises a *nested* `DropCollectionAsync`
   interception inside the delete that emptied it. Left as-is: a collection really is being dropped
   and a policy layer should see it. The wrinkle to document is that an interceptor which permits the
   delete but rejects the drop will throw after the delete has already been applied.

## Step 7 — Fast-path cost `[x] done`

Done 2026-07-26. 8 new tests in `Interception/InterceptionFastPathTests.cs`; suite at 687 passed /
5 environmental / 8 skipped.

- [x] Confirmed by measurement, not inspection: `GC.GetAllocatedBytesForCurrentThread()` around
      1000 iterations (after 200 warm-up iterations to settle JIT/tiering) asserts **exactly zero**
      bytes for the invocation path, the enumeration path, and the streaming entry path. Exact zero
      rather than a threshold, because the path is straight-line code with no legitimate reason to
      allocate — non-zero means something was added to it.
- [x] `CollectionCallInfo` laziness pinned two ways: an enumeration-only interceptor leaves the
      invocation path at zero and vice versa (this is what the **two** separate flags buy over a
      single "any interceptor" flag), and with two matching interceptors both receive the *same*
      instance, so it is built once per call rather than once per interceptor.
- [x] **Two guard tests so the zero-assertions cannot pass vacuously.** `MeasurementHarness_
      DetectsAllocation` proves the harness reports non-zero for a deliberate allocation, and
      `RegisteredInterceptor_DoesAllocate_SoZeroIsMeaningful` proves the same code path allocates
      once an interceptor matches. Without these, a broken harness would turn every zero-assertion
      green while the path regressed.
- [x] Three helpers widened `private` → `internal` purely to make the path directly measurable
      (`RunInvocationInterceptorsAsync`, `RunEnumerationInterceptorsAsync`,
      `BeginInvocationInterception`), with an XML comment on the first recording why.

### Considered and rejected: caching the factory cast

The fast path is a `castclass` (`(MongoDbServiceFactory)_mongoDbServiceFactory`), a field read and a
branch. The cast could be hoisted into a readonly field at construction, but that would move the
failure for a consumer passing a mock `IMongoDbServiceFactory` from first-operation to
construction-time. The existing code already hard-casts inline on the same hot path
(`Disk/DiskRepositoryCollectionBase.cs` uses `((MongoDbServiceFactory)_mongoDbServiceFactory)` for
`CommandMonitor` and `OnCallEnd`), so this changes no cost that was not already being paid, and a
static type-check is nanoseconds. Not worth the behaviour change.

## Step 8 — Documentation `[x] done`

Done 2026-07-26. Both doc surfaces updated per the shared-instructions rule that they are not
alternatives. `docfx build --warningsAsErrors` clean: 10 conceptual files (was 9), 0 warnings.

- [x] `README.md` — new `## Collection interceptors` section, placed before `## Monitor`: contract,
      registration, coverage, timing points, notes, and the `ActionEvent` distinction.
- [x] `docs/articles/collection-interceptors.md` — new article following the existing
      one-file-per-area pattern; added to `docs/articles/toc.yml` after keyset pagination.
- [x] Documented the relationship to `ActionEvent` in both surfaces — "use `ActionEvent` to watch,
      use an interceptor to decide".
- [x] Documented the **coverage boundary** from Step 6, including *why* provider decoration is not
      equivalent (a collection taking the factory in its constructor never goes through the
      provider) — that is the point Platform's request turned on.
- [x] Documented **rejection timing** from Step 4 in both surfaces.
- [x] Documented the `DropEmptyAsync` wrinkle from Step 6 under Caveats.
- [x] Documented that interceptors are effectively singletons and that per-operation state belongs
      in an `AsyncLocal` — with the Blazor Server circuit-lifetime reason, which is the
      non-obvious part.
- [x] Documented "do not call back into a repository collection", matching the XML docs.
- [x] Steered consumers to key on `OperationType` rather than the `Operation` string. This is the
      documentation half of the Step 6 finding: the mislabelled `DeleteOneAsync` stays wrong until
      the follow-up lands, and `OperationType` is correct either way.
- [x] Cross-link uses the GitHub blob form, matching the two existing docs links in `README.md`
      rather than the published-site URL.
- [x] Landed as a separate `docs:` commit.

## Step 9 — Close-out (only on user confirmation)

- [ ] Re-run `dotnet outdated`; apply anything new in this PR.
- [ ] Full test suite green.
- [ ] Mark the `Requests.md` entry Done with date + summary; add the `## Follow-up` line for
      Tharga.Platform naming the version.
- [ ] Record follow-ups in `planned/README.md`: (a) reference latency-simulator interceptor,
      (b) semantic lockable operation names, (c) post-call / on-exception hook if a consumer asks,
      (d) **fix the `DeleteOneAsync` → `nameof(UpdateOneAsync)` mislabel** found in Step 6 — a
      one-word change that also corrects delete reporting in the monitor, so it wants its own PR.
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

Step 5 done — enumeration point fires at the top of `StreamCursorAsync`, once per stream at cursor
open (settled; pinned by a multi-batch test). Both timing points are now live. 5 tests. Suite at
672 passed / 5 environmental / 8 skipped.

Step 6 done — the delegation claim held, so no production code changed. Audit found the lease/scope
classes hold delegates rather than driver handles, which is why commit/release need no special
handling. 7 tests. Suite at 679 passed / 5 environmental / 8 skipped. Two findings recorded in the
step: a pre-existing `DeleteOneAsync` mislabel (deferred, follow-up filed) and a nested
`DropCollectionAsync` interception under `CreateStrategy.DropEmpty` (left as-is, to document).

Step 7 done — fast path proven by measurement at exactly zero allocated bytes, with two guard tests
so the zero-assertions cannot pass vacuously.

Step 8 done — README section + `docs/articles/collection-interceptors.md` + toc entry; docfx clean.

**All ten acceptance criteria in `feature.md` are now met.** Suite at 687 passed / 5 environmental /
8 skipped, with 62 interception tests across four files.

**Next: Step 9 — close-out, and only on the user's confirmation that the feature is done.** Before
that: push the branch so the feature can be tested from origin, and do NOT open the PR yet (the
close-out commit must be the last one on the branch, per the feature workflow). Step 9 itself
re-runs `dotnet outdated`, marks the `Requests.md` entry Done with a `## Follow-up` line for
Tharga.Platform naming 2.14.0, files the four follow-ups in `planned/README.md` (including the
`DeleteOneAsync` mislabel), archives `feature.md` to `done/collection-interceptor.md`, removes
`plan/`, and opens the PR.
