# Feature: extend-lock-buy-more-time

## Goal

Let a holder of an active lock "buy more time" — extend the lock's `ExpireTime` while still holding it, so a long-running job can take a short lock (e.g. 5 min) and keep it alive without releasing/re-acquiring or over-provisioning the initial timeout.

Backlog item "Extend (renew) an active lock — buy more time" (MongoDB.md, Nice + Medium).

## Decisions (confirmed with user)

- **Method:** `ExtendLockAsync(TimeSpan extension, bool force = false)` on `EntityScope<TEntity,TKey>` (from `PickForUpdateAsync`/`PickForDeleteAsync`/`WaitFor*`) and `LockScope<TEntity,TKey>` (from `LockAsync`). `DocumentLease` (multi-doc) deferred.
- **Semantics:** "from now" — a write sets `ExpireTime = DateTime.UtcNow + extension`.
- **Write-throttle (mass-update protection):** at most one DB write per `MinLockExtendInterval` (virtual on the collection, default `TimeSpan.FromSeconds(60)`). Calls inside the window are in-memory no-ops; calls at/after the window write immediately (so an irregular job that extends after 3 min, then needs to again after 6 min, always gets the write through). **No "headroom/safety" skipping** — the throttle is the only suppression.
- **Atomic, may extend after expiry:** single `LockKey`-guarded `UpdateOne`. Succeeds after expiry **iff** the LockKey still matches (no one took it) **and** `AllowDelayedCommit` is true; strict mode (`AllowDelayedCommit == false`) throws `LockExpiredException` on an expired lock. LockKey mismatch → `LockExpiredException`.
- **Return:** `LockExtensionResult { DateTime ExpireTime; bool Extended }` — the live expiry + whether this call actually wrote.
- **`force: true`** bypasses the throttle (guaranteed write attempt; still expiry/LockKey gated).

## Design

- Scopes hold an **optional** `extendAction` closure (`Func<TimeSpan,bool,Task<LockExtensionResult>>`, default null → `ExtendLockAsync` throws `InvalidOperationException`; keeps `EntityScopeBuilder` working). No in-memory entity mutation — `_entity` stays the acquisition image; the result carries the live expiry.
- `LockHandle` (mutable) holds `Current` (the `Lock`, shared with release closures so a later commit sees the extended expiry) and `LastWriteAt` (init = acquisition `LockTime`).
- `ExtendLockCoreAsync(entity, handle, extension, force, session)`:
  1. Throttle: if `!force && now - handle.LastWriteAt < MinLockExtendInterval` → return `{ ExpireTime = handle.Current.ExpireTime, Extended = false }` (no DB).
  2. Strict gate: if `now > Current.ExpireTime && !AllowDelayedCommit` → throw `LockExpiredException`.
  3. Guarded `UpdateOne` (`Id == id && Lock != null && Lock.LockKey == Current.LockKey`, `Set(Lock, Current with { ExpireTime = now + extension })`). Matched 0 → throw `LockExpiredException`.
  4. On success: `handle.Current = newLock`, `handle.LastWriteAt = now`; return `{ ExpireTime, Extended = true }`.

## Acceptance criteria

- A write pushes `ExpireTime` to `≈ now + extension` and returns `Extended = true`; a throttled call (within the interval) returns the existing expiry + `Extended = false` and does NOT hit the DB.
- A commit succeeds after the original TTL would have expired, once extended (proven on a strict-TTL collection).
- After-expiry extend succeeds when LockKey still matches + `AllowDelayedCommit`; strict mode throws `LockExpiredException`.
- Extending after the lock was stolen/released/removed throws `LockExpiredException`.
- Extending after release/commit throws `LockAlreadyReleasedException`; non-extend scope (via `EntityScopeBuilder`) throws `InvalidOperationException`; `extension <= 0` throws `ArgumentException`.
- `force: true` writes even within the throttle window.
- Unit tests (no Mongo) for scope behavior + throttle + integration tests (`Category=Database`). README lockable section + HostSample example.

## Done condition

Pushed `feature/extend-lock-buy-more-time` for review. After user confirms, close-out (archive plan, remove `plan/`, `feat:` commit) + PR → master. Ships in the next release.
