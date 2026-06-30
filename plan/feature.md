# Feature: Persist remote-reported collections in the shared `_monitor` cache

## Goal
Make the database-backed `_monitor` cache the single source of truth for collection metadata. When a remote monitor agent reports a collection, the server persists that report (so both client and server share it and it survives restarts), each record carries the **age of the data** (when it was last reported), and genuinely volatile telemetry (calls, latency, queue/pool metrics) is *not* persisted.

## Scope
- Add `Discovery.Remote` flag and `CollectionInfo.ReportedAt`.
- Extend `MongoDbCollectionCache` bson round-trip: `ReportedAt`, `CollectionTypeName`, `Clean`, `CurrentSchemaFingerprint`, `Index.Defined`.
- `IngestCollectionInfo` persists to the `_monitor`-backed `ICollectionCache` instead of the volatile `_remoteCollections` map.
- Unify reads on the cache (`GetInstanceAsync`, `GetInstancesAsync`, `RefreshStatsAsync`), including the critical stale-sweep fix so persisted remote records aren't wrongly evicted.
- On agent disconnect: keep the persisted record (data + age), drop only live reachability so actions disable. On a genuine collection drop: remove the persisted record too.
- Delete `_remoteCollections` entirely; live reachability maps (`_collectionSources`, `_sourceToConnectionId`) stay in memory.

## Out of scope (stays volatile)
Call recording, latency, `_remoteQueueStates`, `_remotePoolStates`, connection metrics.

## Acceptance criteria
- Agent-reported collections are written to the per-config `_monitor` collection and reload after a server restart.
- Each persisted record exposes a `ReportedAt` age.
- After an agent disconnects, its collections remain visible (from cache) but `CanExecuteActions` returns false; a genuine drop removes them.
- `Clean`, `CurrentSchemaFingerprint`, `CollectionTypeName`, and `Index.Defined` survive a persist→reload cycle (closes the deferred PR #130 cache item).
- Build + full test suite green (excluding the ~8 environmental replica-set tests).

## Done condition
All acceptance criteria met, new/updated tests pass, minor version bumped, README/docs reviewed, PR opened to `master`.
