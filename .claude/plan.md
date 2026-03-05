# Plan: ICollectionCache Service

## Steps

- [x] Step 1: Create feature branch `feature/collection-cache-service` and .claude files
- [x] Step 2: Add `StatsUpdatedAt: DateTime?` and `IndexUpdatedAt: DateTime?` to `CollectionInfo.cs` and `MonitorRecord.cs`
- [x] Step 3: Create `Tharga.MongoDB/Internals/ICollectionCache.cs`
- [x] Step 4: Create `Tharga.MongoDB/Internals/MemoryCollectionCache.cs`
- [x] Step 5: Create `Tharga.MongoDB/Internals/MongoDbCollectionCache.cs` (Bson logic moved from MongoDbService)
- [x] Step 6: Modify `IMongoDbServiceInternal.cs` — removed 4 CRUD methods, added `BaseMongoDatabase`
- [x] Step 7: Modify `MongoDbService.cs` — removed 4 CRUD implementations + Bson helpers, added `BaseMongoDatabase`
- [x] Step 8: Modify `DatabaseMonitor.cs` — injected `ICollectionCache`, replaced all `_cache` usages, added `ResetAsync`
- [x] Step 9: Modify `IDatabaseMonitor.cs` and `DatabaseNullMonitor.cs` — added `ResetAsync`
- [x] Step 10: Modify `MongoDbRegistrationExtensions.cs` — registers `ICollectionCache` by StorageMode
- [x] Step 11: Modify `CollectionView.razor` — added Reset button + NotificationService inject
- [x] Step 12: Build succeeded. 175 tests: 14 pre-existing lockable failures (unchanged), 153 passing.

- [x] Step 13: Added unit tests — 10 MemoryCollectionCache tests + 23 MongoDbCollectionCache Bson serialization tests
- [x] Step 14: Fixed Fingerprint Match "Outdated" — compute `CurrentSchemaFingerprint` from code when missing on cache-loaded entries
- [x] Step 15: Removed AccessCount from cache storage (Bson), CollectionView list, CollectionDialog, CollectionModel, and MonitorRecord
- [x] Step 16: Added StatsUpdatedAt/IndexUpdatedAt tooltips on Document Count, Size, and Indices in CollectionDialog
- [x] Step 17: Set `Source.Monitor` flag on entries loaded from MongoDB cache in `InitializeAsync`
- [x] Step 18: Fixed indices "not defined in code" — enrich cache-loaded entries with code-derived defined indices, registration, and source in `GetCollectionsFromDb`

- [x] Step 19: Simplified ICollectionCache to persistence-only (LoadAllAsync, SaveAsync, DeleteAsync, ResetAsync). Removed dictionary from MongoDbCollectionCache — it now only handles DB persistence. Moved ConcurrentDictionary back into DatabaseMonitor.
- [x] Step 20: Removed CallCount from Bson serialization — per-session only, not persisted.
- [x] Step 21: Added deserialization failure handling in MongoDbCollectionCache.LoadAllAsync — drops _monitor collection and logs warning if any documents fail to deserialize.

- [x] Step 22: Major restructuring of CollectionInfo and ICollectionCache:
  - Created `CollectionStats` entity grouping DocumentCount (long), Size, UpdatedAt
  - Added `UpdatedAt` to `IndexInfo` (grouping index data with its timestamp)
  - Removed `AccessCount`, `CallCount`, `DocumentCount` (record type), `Size`, `StatsUpdatedAt`, `IndexUpdatedAt` from CollectionInfo
  - Renamed `Types` → `EntityTypes` in CollectionInfo, MonitorRecord, DatabaseMonitor, Blazor components
  - Deleted `DocumentCount.cs` — replaced by `CollectionStats.DocumentCount` (long)
  - Expanded `ICollectionCache` with in-memory methods (TryGet, AddOrUpdate, Set, TryRemove, GetAll, GetKeys, Clear)
  - Updated both `MemoryCollectionCache` and `MongoDbCollectionCache` to own ConcurrentDictionary
  - Removed `_dict` from `DatabaseMonitor` — now uses `_cache` for everything
  - Updated Blazor components: removed Calls column, added Clean column to CollectionView table
  - Updated CollectionDialog: "Entity Types" label, Stats-based tooltips
  - Updated all Bson serialization for new entity structure (backward compatible: reads "Types" field from Bson)
  - Updated all tests (36 tests pass: 15 MemoryCollectionCache + 21 MongoDbCollectionCacheBson)
  - Build succeeds. 188 passing tests, 15 pre-existing lockable failures (unchanged).

- [x] Step 23: CleanInfo — single source of truth from `_clean`
  - Added `ReadAllCleanInfoAsync(databaseName)` to `IMongoDbService` + `MongoDbService` (batch reads all `_clean` docs)
  - Batch-load CleanInfo in `GetCollectionsFromDb` — always sets Clean from `_clean` on every entry
  - Removed per-collection `ReadCleanInfoAsync` from `RefreshStatsAsync` and `LoadAndCacheAsync` — they now preserve existing cached Clean
  - Added yellow highlighting for unknown clean status (`css-clean`) in `CollectionView.razor`
  - Build succeeds. 188 passing tests, 15 pre-existing lockable failures (unchanged).

## Last session
Step 23 complete. CleanInfo now loaded from `_clean` during list scan (batch per database). RefreshStatsAsync and LoadAndCacheAsync no longer read from `_clean` — they preserve cached CleanInfo. Unknown clean status shows yellow in CollectionView.
