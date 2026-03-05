# Features / TODOs

## ~~1. Per-collection initialization lock~~ ✓ Done
**File:** `Tharga.MongoDB/Disk/DiskRepositoryCollectionBase.cs:30`

The `_fetchLock` semaphore is `static`, meaning all collections globally compete on a single lock during initialization. It should be scoped per collection `fullName` so unrelated collections do not block each other.

**Task:** Replace the global `SemaphoreSlim` with a `ConcurrentDictionary<string, SemaphoreSlim>` keyed by collection full name in `FetchCollectionAsync`.

---

## 2. GetDerived / GetGeneric polymorphic query support
**File:** `Tharga.MongoDB/Disk/DiskRepositoryCollectionBase.cs:33`

No way to query a collection using a base type and get back derived types. Only the exact declared type `TEntity` is loaded.

**Task:** Add `GetDerivedAsync<TBase>()` or `GetGenericAsync()` methods that use `BsonDocument`-level queries and deserialize to the correct subtype based on a discriminator field.

---

## 3. Explain mode for query analysis
**File:** `Tharga.MongoDB/Disk/DiskRepositoryCollectionBase.cs:128-129`

No way to inspect the MongoDB query execution plan (index usage, scan cost) from within the library. The filter/predicate details are also not captured in logging.

**Task:** Add an opt-in explain mode (via options or configuration) that calls MongoDB's `explain` command and logs/returns the execution plan. Also enrich the step trace with the rendered filter string.

---

## 4. Index blocker identification
**File:** `Tharga.MongoDB/Disk/DiskRepositoryCollectionBase.cs:1022`

`GetIndexBlockers` is stubbed out and throws. The goal is to return groups of document IDs that prevent a unique index from being created.

**Task:** Implement `GetIndexBlockers(indexName)` by running an aggregation that groups documents by the indexed fields and returns those with `count > 1`.

---

## 5. Replace collection provider cache with ICollectionPool
**File:** `Tharga.MongoDB/Internals/CollectionProvider.cs:32`

`GetGenericDiskCollection` uses a local `_collectionProviderCache` instead of the injected `ICollectionPool`, making it inconsistent with how the rest of the system manages collection instances.

**Task:** Refactor `GetGenericDiskCollection` to use `ICollectionPool` for consistency and to benefit from whatever pooling/lifecycle management it provides.

---

## 6. Conditional cache decision in MongoDbServiceFactory
**File:** `Tharga.MongoDB/Internals/MongoDbServiceFactory.cs:48`

Caching is hardcoded to `true`. The original intent was to conditionally disable caching when a `DatabaseName` fingerprint is present (multi-tenant / per-request databases), but the logic was commented out pending a clean solution.

**Task:** Restore the conditional cache check — disable caching when the database context carries a dynamic `DatabaseName` (e.g. via `ICollectionFingerprint`) so per-tenant databases get fresh connections.

---

## 7. Clean helpers for sub-objects and sub-collections on PersistableEntityBase
**File:** `Tharga.MongoDB/PersistableEntityBase.cs:10`

No built-in helpers to propagate the `NeedsCleaning` / `EndInit` lifecycle to nested objects or embedded collections within an entity.

**Task:** Add virtual `CleanSubObjects()` and `CleanSubCollections()` helpers on `PersistableEntityBase` that recursively call `NeedsCleaning` / `EndInit` on nested `PersistableEntityBase` properties and `IEnumerable<PersistableEntityBase>` fields.
