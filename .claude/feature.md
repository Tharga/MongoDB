# Feature: ICollectionCache Service

## Goal
Refactor `DatabaseMonitor`'s internal cache into a proper `ICollectionCache` service with two implementations, allowing cache storage to be swapped based on configuration.

## Scope
- New `ICollectionCache` interface (internal)
- `MemoryCollectionCache` — wraps ConcurrentDictionary, no persistence
- `MongoDbCollectionCache` — wraps ConcurrentDictionary + persists to `_monitor` collection, loads on startup
- Selected by existing `MonitorStorageMode` setting
- `ResetAsync()` on `IDatabaseMonitor` — clears both memory and MongoDB storage
- Reset button in `CollectionView.razor`
- `StatsUpdatedAt` and `IndexUpdatedAt` timestamps on `CollectionInfo`

## Acceptance Criteria
- [ ] `ICollectionCache` interface with `TryGet`, `GetAll`, `AddOrUpdate`, `Remove`, `InitializeAsync`, `SaveAsync`, `DeleteAsync`, `ResetAsync`
- [ ] `MemoryCollectionCache` passes all operations to ConcurrentDictionary; persistence methods are no-ops
- [ ] `MongoDbCollectionCache` loads all records on startup from base configured database(s)
- [ ] Dynamic collections survive app restart when using Database storage mode
- [ ] `IDatabaseMonitor.ResetAsync()` clears cache (memory + DB) and implemented in DatabaseMonitor and DatabaseNullMonitor
- [ ] Reset button in CollectionView.razor calls ResetAsync and reloads
- [ ] 4 monitor CRUD methods removed from `IMongoDbServiceInternal`
- [ ] All existing tests pass
- [ ] Solution builds without errors

## Done Condition
All acceptance criteria met, tests green, README updated if needed.
