# Plan: Persist remote-reported collections in the shared `_monitor` cache

Tracking file for the feature. See `plan/feature.md` for scope/acceptance.

## Steps

- [x] 0. Create feature branch `feature/persist-remote-collection-cache` from `master`; commit the pending `.claude/settings.json` change as a separate `chore:` commit.
- [x] 1. Apply NuGet updates up front (test/sample projects only), verify build + full test suite green as the baseline. (Baseline: 625 passed, 6 environmental replica-set failures.)
  - Tharga.MongoDB.Tests: JetBrains.Annotations 2025.2.4 → 2026.2.0, Microsoft.NET.Test.Sdk 18.6.0 → 18.7.0
  - HostSample: Swashbuckle.AspNetCore 10.2.1 → 10.2.3
- [x] 2. Data model: add `Discovery.Remote = 8`; add `CollectionInfo.ReportedAt` (DateTime?).
- [x] 3. Persistence round-trip in `MongoDbCollectionCache`: persist/restore `ReportedAt`, `CollectionTypeName` (display name via new `CollectionTypeDisplayName` field), `Clean`, `CurrentSchemaFingerprint`, `Index.Defined`. Legacy-doc tolerance kept.
- [x] 4. `IngestCollectionInfo`: set `ReportedAt`, OR `Discovery.Remote`, write via `_cache.Set` + `_cache.SaveAsync`; source/connection maps + event kept.
- [x] 5. Read paths: `GetInstanceAsync` (remote-origin returns cached, no DB probe); `GetInstancesAsync` (stale-sweep skips remote-origin; yields persisted entries not scanned); `RefreshStatsAsync` guard switched to local-config check + stamps `ReportedAt`.
- [x] 6. Disconnect keeps persisted data (drops only reachability); `IngestCollectionDropped` removes persisted record (`_cache.TryRemove` + `DeleteAsync`).
- [x] 7. Removed `_remoteCollections` field; `ResetAsync` relies on `_cache.ResetAsync()`; all references migrated.
- [x] 8. Tests: extended bson round-trip (new fields + legacy doc), rewired `RemoteCollectionReachabilityTests` + `RemoteActionDelegationTests` to a real `MemoryCollectionCache`, new disconnect/keep-data + ingest semantics tests. Full suite: 629 passed, same 6 environmental failures.
- [~] 9. Minor version bump; README/docs review (separate `docs:` commit at close-out). Consider surfacing `ReportedAt` in the Blazor collections view.

## Last session
Steps 0–8 complete and committed. Core feature works: agent reports persist into the `_monitor`-backed cache with a `ReportedAt` age, survive restart, stay visible (actions gated off) after disconnect, and are removed on a genuine drop. Next: version bump + docs review, and decide on UI surfacing of `ReportedAt`.
