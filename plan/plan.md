# Plan: CancellationToken on the write methods (#166)

## Steps
- [x] 1. Dependencies: .NET 10.0.14 patches, Tharga.Communication 1.1.3, Tharga.Console 4.2.5; SignalR floor removed (Communication ships 10.0.13). `c91d9e6`.
- [x] 2. Signatures and bodies: interfaces, abstract declarations, disk implementations pass the token into `ExecuteAsync` (every lambda already passed `ct` to the driver); lockable overrides.
  - Implementation came before the tests this time; the tests could not have compiled without the new parameter.
- [x] 3. Tests: `WriteCancellationTests` (16 write paths with a cancelled token + the options fix), `Lockable/DeleteOneCancellationTests`. The options test was checked against the old line: fails without the fix, passes with it. Full suite 853, 845 passed, 8 skipped.
- [x] 4. Version 2.19 (`MAJOR_MINOR`), docs (README, getting-started), commits.
- [~] 5. Close-out: issue comment on #166 at release, Requests.md follow-up for the cascade (Team.MongoDB, Cache.MongoDB rebuild), archive plan, remove `plan/`, PR.

## Notes
- Plan files written after the design decision and implementation (skipped up front); kept for the record.

## Last session
2026-10-25: steps 1-4 done and committed locally. Next: close-out on the user's word.
