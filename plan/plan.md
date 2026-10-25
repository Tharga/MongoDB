# Plan: flaky lock-expiry tests

## Steps
- [x] 1. Dependencies: `dotnet outdated` offers only `MongoDB.Driver` 3.13.0, which is held at 3.9.0 (#158). Nothing to apply.
- [x] 2. Baseline: run the lockable tests repeatedly and record which fail.
  - 15 serial runs of the Lockable namespace: 0 failures. Local round trips take ~6 ms, so the second check never lands in the pick's millisecond. A throwaway probe (not committed) forced the window instead: seed a lock expiring on a millisecond boundary, spin until just past it, pick. Without the wait 10 of 200 read as held, message `is locked by 'seed' for -00:00:00.0009040` (same shape as the backlog note); with `WaitForNextMillisecond` 0 of 200.
- [x] 3. Helper `MongoClock.WaitForNextMillisecond()` in `Tharga.MongoDB.Tests/Support`, called after the zero-timeout pick in the six affected tests.
- [x] 4. Re-run the lockable tests repeatedly (no expiry failures), then the full suite.
  - Full suite 835, 827 passed, 8 skipped. Repeated lockable runs skipped: the baseline already showed none here, so the probe is the evidence.
- [ ] 5. Records (at close): backlog entries (Important + Easy item, and the two cleanup-sweep entries).

## Notes
- Cause and the reason for not changing the library are in `feature.md`.

## Last session
2026-10-25: steps 1-4 done and committed locally. Next: close-out on the user's word (backlog records, archive plan, remove `plan/`, push, PR).
