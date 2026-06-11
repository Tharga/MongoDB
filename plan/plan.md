# Plan: extend-lock-buy-more-time

## Steps

- [x] 1. `EntityScope<T,TKey>` — optional `extendAction` (`Func<TimeSpan,bool,Task<LockExtensionResult>>`), `ExtendLockAsync(extension, force)`; `EntityScope<T>` ctor threaded
- [x] 2. `LockScope<T,TKey>` — same changes; `LockScope<T>` ctor threaded
- [x] 3. `LockableRepositoryCollectionBase` — `MinLockExtendInterval` virtual (default 60s), `LockHandle` (+ `LastWriteAt`), `ExtendLockCoreAsync` (throttle + strict gate + LockKey guard), wired in `CreateLockAsync` + `BuildLockScope`; new `LockExtensionResult` record
- [x] 4. Unit tests (no Mongo): 8 in `ExtendLockScopeTests` — delegation (ext+force), result pass-through, arg validation, released/not-supported guards
- [x] 5. Integration tests (`Category=Database`): 7 in `ExtendLockTests` — write pushes expiry/keeps key/persists; throttle no-op; force bypass; strict-commit-past-TTL; after-expiry success (delayed) + throw (strict); stolen → throw
- [x] 6. Build + tests: core net8/9/10 clean; 271 non-DB pass; 167 lockable DB pass (incl. 7 new). The 5 TransactionsTests failures are standalone-Mongo-no-replica-set, pre-existing, unrelated.
- [x] 7. Docs: README "Extending a lock" subsection + HostSample `ProcessLongRunningAsync` example
- [~] 8. Commit code + tests + docs + plan; push for review
- [ ] 9. (After user confirms) close-out: archive plan -> done/, git rm -r plan, `feat:` commit, PR

## Last session

2026-06-10 — Implemented on `feature/extend-lock-buy-more-time` (off origin/master). Design iterated with the user: dropped the "never skip near expiry" safety clause; the protection is a pure **write-throttle** (`MinLockExtendInterval`, default 60s, virtual) — calls inside the window are in-memory no-ops, calls at/after it write immediately (handles irregular jobs). `ExtendLockAsync(extension, force=false)` returns `LockExtensionResult { ExpireTime, Extended }`. Atomic LockKey-guarded write; after-expiry extend allowed when unstolen + `AllowDelayedCommit` (strict mode throws). All builds + tests green. Next: push for review; close-out + PR after user confirms.
