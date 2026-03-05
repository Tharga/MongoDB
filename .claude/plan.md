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

## Last session
Steps 13-18 complete. Tests: 186 passing, 14 pre-existing lockable failures. Build succeeds across all targets.
