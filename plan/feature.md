# Feature: flaky lock-expiry tests

Branch: `fix/lock-expiry-test-flakiness`. Backlog item "The lock-expiry tests are flaky on a sub-millisecond
boundary" (Important + Easy), plus the two related cleanup-sweep entries (`GetLockedExpired`,
`DeleteWhenOneIsExpired`).

## Cause

MongoDB stores dates at millisecond precision. `AcquireLockAsync` and the expired/unlocked queries compare
`Lock.ExpireTime` with a `now` that is truncated to the millisecond when serialized, as is the stored
`ExpireTime`. A test that picks with `TimeSpan.Zero` and checks expiry within the same millisecond therefore
sees the lock as still held (`floor(T0) < floor(T1)` is false), while the in-memory message computes a negative
remaining time from the full-precision `now` (`-00:00:00.0008418` in the backlog note).

The library is right to behave this way: a lock reads as held until the next millisecond after its expiry -
at most 1 ms late, never early, which is the safe direction for a lock. No library change.

## Scope

- Test helper that waits until the clock is in a later millisecond than when it was called.
- Call it after the zero-timeout pick in the tests that then check expiry on the server:
  `GetLockedTests.GetLockedExpired`, `GetUnlockedTests` (timed-out entity), `DeleteManyTests.DeleteWhenOneIsExpired`,
  `PickTests.PickEntityWithExpiredLock`, `PickSortTests.SortedPickReclaimsExpiredLockWhenItIsSortFirst`,
  `UpdateManyTests` (expired entity).
- Not affected: release/commit "too late" tests (client-side check at full precision; delayed commit matches on
  `LockKey`), `WaitForUpdateTests` (retries until expired).

## Acceptance criteria
- Repeated runs of the lockable tests show no expiry-related failures.
- Full suite passes; backlog entries updated.

## Done condition
User confirms; PR merged.
