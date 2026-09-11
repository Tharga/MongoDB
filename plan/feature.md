# Feature: ExecuteLimiter queue bounds

Closes [#155](https://github.com/Tharga/MongoDB/issues/155) — *ExecuteLimiter wait queue is unbounded;
bursts OOM-kill the host process.*

## Goal

Give a burst of database work a way to become a throttling event instead of a process-killing memory event.

## Background

`ExecuteLimiter.ExecuteAsync` queues callers on a per-pool `SemaphoreSlim` with no bound. Every waiter pins
its async state machine and captured data, so under a burst the queue grows until the host kills the process.
The reporter measured ~11,900 queued operations against a pool of 500, a ~6 GB heap, and roughly three hours
of restart-looping; it has happened three times in production.

Neither existing knob helps: `Enabled = false` removes throttling entirely, and `MaxPoolSizeOverride` bounds
concurrency, which under the same inbound rate makes the queue grow *faster*.

## The correction to the proposed fix

The issue proposes rejecting when `state.GetQueued() > MaxQueueLength`. That counter is the wrong one to cap.
**Every** caller increments `_queued` before touching the semaphore and only decrements after acquiring a
slot, so it counts everything transiting the admission path rather than callers that actually wait. Capping
it directly would mean:

- `MaxQueueLength = 0` rejects every call, including against a completely idle pool.
- `MaxQueueLength = 10` on a pool of 500 rejects the 11th concurrent caller with 489 slots free.

So the bound is applied to a **new `_waiting` counter**, incremented only when a non-blocking
`Semaphore.Wait(0)` fails — i.e. only by callers that genuinely have to queue. `_queued` keeps its present
meaning untouched, so no monitor number shifts under anyone.

A second, smaller correction: the cap tests the value **returned by** the increment rather than a follow-up
`GetQueued()` read. The re-read is racy in both directions under concurrent arrivals; the returned value
gives each arrival a distinct ordinal, so admission is deterministic.

## Scope

**In:**

1. `ExecuteLimiterOptions.MaxQueueLength` (`int?`) and `QueueTimeout` (`TimeSpan?`), both `null` by default —
   exactly today's behaviour.
2. `ExecuteLimiterException` base with `ExecuteLimiterQueueFullException` and
   `ExecuteLimiterQueueTimeoutException`, so a caller can shed load with one `catch` and still tell the two
   apart.
3. The non-blocking fast path plus the `_waiting` counter the cap is applied to.
4. Option validation at limiter construction.
5. `PoolQueueState.RejectedCount`, and an **edge-triggered** warning — first rejection per pool, re-armed once
   that pool's queue drains.
6. Docs on both surfaces.

**Out:**

- Plumbing `RejectedCount` through `ConnectionPoolStateDto` → forwarder → Blazor → MCP. The local
  `IQueueMonitor` surface carries it; the remote chain is a larger ripple and no one has asked.
- Any change to `Enabled = false`, which the reporter already established is not a mitigation.
- A global (cross-pool) cap. The limiter is per-pool by design, and #155 asks for a per-pool bound.

## Design decisions

- **The warning is edge-triggered, not per rejection.** A sustained flood is exactly the scenario here, and
  one log line per rejected operation would reproduce the original failure in the logging pipeline — 11,900
  warnings for the episode that motivated the issue. First rejection per pool logs; the pool re-arms when its
  waiting count returns to zero.
- **Rejection is an exception, not a result type.** It matches the `ResultLimitException` precedent for the
  other previously-unbounded collection in this library, and a caller that ignores the return value of a shed
  is a caller that has silently lost data.
- **Nonsensical configuration throws at construction.** A negative `MaxQueueLength` or a non-positive
  `QueueTimeout` otherwise surfaces as "no throttling happened" weeks later, which is indistinguishable from
  the bug this feature fixes.

## Acceptance criteria

- [ ] With `MaxQueueLength = 0` and an idle pool, calls still execute — nothing is rejected while slots are
      free. This is the flaw in the literal proposal, asserted directly.
- [ ] With the pool saturated and `MaxQueueLength = N`, the first N waiters queue and the next is rejected
      with `ExecuteLimiterQueueFullException` naming the pool and the limit.
- [ ] A rejected call leaves `_queued`, `_waiting`, `_totalQueueCount` and the in-flight set exactly as it
      found them — a rejected burst must not leak queue depth.
- [ ] `QueueTimeout` throws `ExecuteLimiterQueueTimeoutException` when a waiter exceeds it, and does not leak
      a semaphore slot.
- [ ] Both options `null` reproduce current behaviour, including unbounded queueing.
- [ ] `RejectedCount` counts rejections per pool; the warning fires once per pressure episode, not once per
      rejection.
- [ ] Negative `MaxQueueLength` / non-positive `QueueTimeout` throw at construction.
- [ ] Full suite green apart from the known pre-existing failures; build clean at 0 warnings.

## Done condition

All acceptance criteria met, both doc surfaces updated, `MAJOR_MINOR` bumped to `2.18`, issue #155 answered
and closed.

## Baseline (before any change on this branch)

`dotnet test -c Release` on `master`: **770 total, 755 passed, 8 skipped, 7 failed** — 5 `TransactionsTests`
needing a replica set, plus `PickEntityWithExpiredLock` and `DeleteWhenOneIsExpired` from the flaky
lock-expiry family. All recorded in the backlog. Build: 0 warnings.
