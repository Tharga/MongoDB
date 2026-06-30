# Plan: Persist remote-reported collections in the shared `_monitor` cache

Tracking file for the feature. See `plan/feature.md` for scope/acceptance.

## Steps

- [x] 0. Create feature branch `feature/persist-remote-collection-cache` from `master`; commit the pending `.claude/settings.json` change as a separate `chore:` commit.
- [~] 1. Apply NuGet updates up front (test/sample projects only), verify build + full test suite green as the baseline.
  - Tharga.MongoDB.Tests: JetBrains.Annotations 2025.2.4 → 2026.2.0, Microsoft.NET.Test.Sdk 18.6.0 → 18.7.0
  - HostSample: Swashbuckle.AspNetCore 10.2.1 → 10.2.3
- [ ] 2. Data model: add `Discovery.Remote = 8`; add `CollectionInfo.ReportedAt` (DateTime?).
- [ ] 3. Persistence round-trip in `MongoDbCollectionCache`: persist/restore `ReportedAt`, `CollectionTypeName` (display name), `Clean`, `CurrentSchemaFingerprint`, `Index.Defined`. Keep legacy-doc tolerance.
- [ ] 4. `IngestCollectionInfo`: set `ReportedAt`, OR `Discovery.Remote`, write via `_cache.Set` + `_cache.SaveAsync`; keep source/connection maps + event.
- [ ] 5. Read paths: `GetInstanceAsync` (remote-origin returns cached, no DB probe); `GetInstancesAsync` (scope stale-sweep to local-origin only; yield remote-origin cache entries); `RefreshStatsAsync` guard + set `ReportedAt`.
- [ ] 6. Disconnect keeps persisted data (drop only reachability); `IngestCollectionDropped` removes persisted record (`_cache.TryRemove` + `DeleteAsync`).
- [ ] 7. Remove `_remoteCollections` field; fix `ResetAsync`; migrate all 10 references.
- [ ] 8. Tests: bson round-trip (new), rewrite `RemoteCollectionReachabilityTests` + `RemoteActionDelegationTests` to real cache + new disconnect semantics, new ingest/read tests.
- [ ] 9. Minor version bump; README/docs review (separate `docs:` commit at close-out).

## Last session
Branch created (step 0 done). settings.json committed (chore). Starting step 1 (NuGet updates + baseline build/test).
