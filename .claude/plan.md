# Plan: Remote collection metadata in Monitor.Server

## Steps

### 1. Enrich MonitorCollectionInfoMessage ✓
- [x] Added Server, DatabasePart, Discovery, Registration, EntityTypes, Stats, Index, Clean
- [x] All strings/records — no `Type` references, fully serializable

### 2. Client — forward collection info ✓
- [x] `MonitorForwarder` subscribes to `IDatabaseMonitor.CollectionInfoChangedEvent`
- [x] Builds `MonitorCollectionInfoMessage` from `CollectionInfo` and forwards via `PostAsync`
- [x] No bulk-send at startup — forwards as collections are discovered (handles late config)
- [x] Updated tests for new constructor parameter

### 3. Server — handler and storage
- [ ] Create `MonitorCollectionInfoHandler : PostMessageHandlerBase<MonitorCollectionInfoMessage>`
- [ ] Add `IngestCollectionInfo(MonitorCollectionInfoMessage)` to `IDatabaseMonitor`
- [ ] Store remote collections in `ConcurrentDictionary<string, CollectionInfo>` keyed by fingerprint

### 4. Merge in GetInstancesAsync
- [ ] After loading local collections, append remote-only ones (not already present locally)
- [ ] Deduplication: local wins when fingerprint matches

### 5. Registration enum — add Remote value
- [ ] Add `Registration.Remote` enum value
- [ ] Remote-ingested collections use this value
- [ ] CollectionView/CollectionDialog: hide action buttons when Registration == Remote

### 6. Tests
- [ ] Ingested collection appears in GetInstancesAsync
- [ ] Duplicate fingerprint from remote is deduplicated (local wins)
- [ ] Remote collection has Registration.Remote

### 7. Final validation
- [ ] Full test suite passes
- [ ] Solution builds in Release
- [ ] Commit and summarize
