# Plan: ExecuteLimiter queue bounds (#155)

Branch: `feature/execute-limiter-queue-bounds`

## Steps

- [x] **1. Dependency check** — `dotnet outdated` across the solution (excluding the held `MongoDB.Driver`,
      #158) reports nothing outdated; yesterday's close-out pass left everything current, so there is no
      `chore(deps)` commit this time. Baseline captured: 770 tests, 755 passed, 8 skipped, 7 failed, all
      pre-existing.

- [x] **2. Options and exceptions** — `MaxQueueLength` / `QueueTimeout` on `ExecuteLimiterOptions`;
      `ExecuteLimiterException` base with `ExecuteLimiterQueueFullException` and
      `ExecuteLimiterQueueTimeoutException`, both carrying the `ServerKey`.

- [x] **3. The bound itself** — `_waiting` / `_rejected` / `_rejectionWarned` on `PerPoolState`; the
      `Semaphore.Wait(0)` fast path in `ExecuteAsync`; `RejectIfQueueFull` on the returned waiting count; the
      existing `catch (!acquired)` path extended to release `_waiting`; constructor validation.

      **Two defects caught during implementation, both fixed and both now pinned by a test.**
      (a) `waitingCount <= _maxQueueLength` is a *lifted* comparison — false when the limit is null — so the
      default unbounded configuration would have rejected every waiter. Now an explicit null check.
      (b) Re-arming the warning on the rejection unwind meant `MaxQueueLength = 0`, where a rejected caller is
      the only waiter there ever is, warned on every single rejection — the flood the edge trigger exists to
      prevent. The warning now re-arms only when the queue drains through work.

- [x] **4. Monitor surface** — `PoolQueueState.RejectedCount`, non-required so the record stays additive.

- [x] **5. Tests** — `ExecuteLimiterQueueBoundTests`, 18 tests covering all of the above.

- [x] **6. Docs** — README Execute Limiter table plus a Backpressure section with the shed-load pattern;
      `docs/articles/monitoring.md` queue section plus its own Backpressure subsection.

- [x] **7. Version bump** — `MAJOR_MINOR` `2.17` → `2.18`.

- [x] **8. Verify** — 0 warnings; 788 tests, 774 passed, 8 skipped, 6 failed — the 5 environmental
      `TransactionsTests` plus one member of the flaky lock-expiry family. `PickTests` passes 26/26 in
      isolation, and a repeat run of identical binaries failed a *different* member of that family, which is
      the documented flaky signature rather than a regression.

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
still parked awaiting approval, so this stacks a second unreleased minor behind it). Steps 1-8 done.
Implementation complete; awaiting the user's test of the pushed branch before close-out.
