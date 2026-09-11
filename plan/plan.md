# Plan: ExecuteLimiter queue bounds (#155)

Branch: `feature/execute-limiter-queue-bounds`

## Steps

- [x] **1. Dependency check** — `dotnet outdated` across the solution (excluding the held `MongoDB.Driver`,
      #158) reports nothing outdated; yesterday's close-out pass left everything current, so there is no
      `chore(deps)` commit this time. Baseline captured: 770 tests, 755 passed, 8 skipped, 7 failed, all
      pre-existing.

- [~] **2. Options and exceptions**
  - `ExecuteLimiterOptions`: `MaxQueueLength` (`int?`), `QueueTimeout` (`TimeSpan?`).
  - `ExecuteLimiterException` base + `ExecuteLimiterQueueFullException` + `ExecuteLimiterQueueTimeoutException`,
    beside the existing `ResultLimitException`.

- [ ] **3. The bound itself**
  - `PerPoolState`: `_waiting` and `_rejected` counters, plus the edge-trigger flag for the warning.
  - `ExecuteAsync`: non-blocking `Semaphore.Wait(0)` fast path; only on failure increment `_waiting`, apply
    the cap against the returned value, and await with or without the timeout.
  - Unwind through the existing `catch (!acquired)` path, extended to release `_waiting`.
  - Re-arm the warning when a pool's waiting count returns to zero.
  - Validate the options in the constructor.

- [ ] **4. Monitor surface** — `PoolQueueState.RejectedCount`, populated from the per-pool counter.

- [ ] **5. Tests**
  - Idle pool + `MaxQueueLength = 0` still executes (the flaw in the literal proposal).
  - Saturated pool admits N waiters and rejects the next.
  - Rejection leaves every counter and the in-flight set unchanged.
  - Timeout throws and does not leak a slot.
  - Both options null = unbounded, current behaviour.
  - `RejectedCount` increments; the warning is edge-triggered, not per rejection.
  - Construction validation.

- [ ] **6. Docs** — README Execute Limiter table + a backpressure section; `docs/articles/monitoring.md`
      queue section.

- [ ] **7. Version bump** — `MAJOR_MINOR` `2.17` → `2.18`.

- [ ] **8. Verify** — 0 warnings; suite green apart from the known pre-existing failures.

- [ ] **9. Push and hand over for testing** — do not open the PR yet.

## Close-out (only once the user confirms the feature is done)

- [ ] Re-run `dotnet outdated` and apply anything new.
- [ ] `Requests.md` — add a Done entry under `## Tharga.MongoDB` with evidence, and a `## Follow-up` line if
      the reporting project is a Tharga one.
- [ ] Comment on #155 with the options, the semantics correction and the version, then close it (or let the
      PR's `Closes #155` do it).
- [ ] Archive `plan/feature.md` to `$DOC_ROOT/Tharga/plans/Toolkit/MongoDB/done/execute-limiter-queue-bounds.md`
      and add it to `planned/README.md`'s Done list.
- [ ] `git rm -r plan`, final commit `feat: execute limiter queue bounds complete`, push, open PR.

## Last session

2026-09-11 — Branch created off `master` (level with `origin/master`; 2.17.0 merged but its release job is
still parked awaiting approval, so this stacks a second unreleased minor behind it). Step 1 done.
