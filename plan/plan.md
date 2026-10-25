# Plan: reported issues, October 2026

## Steps
- [x] 1. Dependencies: apply all NuGet updates except `MongoDB.Driver`, put the driver back at 3.9.0 (#158), build + full test suite. Commit `chore(deps): nuget update`.
  - Done in `9647e63`. Builds clean on 3.9.0 (nothing since September depends on 3.10+ APIs); 811 tests, 803 passed, 8 skipped. Added `DriverCompatibilityTests` so a future sweep that raises the driver fails the build. Also floored `Microsoft.AspNetCore.SignalR.Common` 10.0.13 in Monitor.Client: Tharga.Communication 1.1.2 (latest) pulls 10.0.12, affected by CVE-2026-70159.
- [x] 2. `GetCollection()` deadlock: failing test on a single-threaded synchronization context, then the `Task.Run` fix and the `[Obsolete]` message.
  - `GetCollectionDeadlockTests`: both the blocked caller and a later `CountAsync` on the same collection hung before the fix (10 s timeout), pass in 1.5 s after. Fix is `Task.Run(() => FetchCollectionAsync())` in `GetCollection()`; the `[Obsolete]` text on class and interface now says the call blocks, shared as `Constants.GetCollectionObsoleteMessage`. Library-wide `ConfigureAwait(false)` not needed for this report. 813 tests, 805 passed, 8 skipped.
- [~] 3. #171: sanitizer tests, sanitizer, apply at build site, access event, agent ingest and cache load; MCP resource test.
- [ ] 4. #172: tests for the log-level hook, then `GetExceptionLogLevel` on both catch sites (check lockable collections reach it).
- [ ] 5. Docs: README + `docs/articles` (deadlock note, server identity, log-level hook, driver floor and Cosmos 4.2 note).
- [ ] 6. Records: Requests.md (Neurolito entry), GitHub #171, #172 comments, #158/#160 status. Outward-facing text shown to the user first.

## Notes
- Driver research (2026-10-25): 3.9.0 = last with `minWireVersion: Server42`; 3.10.0-3.13.0 need `Server44`. Cosmos RU offers 5.0/6.0/7.0, which any 3.x driver can reach, but Microsoft documents no in-place upgrade path from 4.2.
- Possible separate bug, not in scope: `DatabaseMonitor.GetCollectionsWithFailedIndices` joins `CollectionInfo.Server` (host:port) to the initiation library's `ServerName` (host only), so it may never match.

## Last session
2026-10-25: branch and plan created; nothing implemented yet. Waiting for plan confirmation.
