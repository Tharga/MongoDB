# Features / TODOs

## 1. GetDerived / GetGeneric polymorphic query support
**File:** `Tharga.MongoDB/Disk/DiskRepositoryCollectionBase.cs:33`

No way to query a collection using a base type and get back derived types. Only the exact declared type `TEntity` is loaded.

**Task:** Add `GetDerivedAsync<TBase>()` or `GetGenericAsync()` methods that use `BsonDocument`-level queries and deserialize to the correct subtype based on a discriminator field.

---

## 2. Index blocker identification
**File:** `Tharga.MongoDB/Disk/DiskRepositoryCollectionBase.cs:1022`

`GetIndexBlockers` is stubbed out and throws. The goal is to return groups of document IDs that prevent a unique index from being created.

**Task:** Implement `GetIndexBlockers(indexName)` by running an aggregation that groups documents by the indexed fields and returns those with `count > 1`.

---

## 3. Replace collection provider cache with ICollectionPool
**File:** `Tharga.MongoDB/Internals/CollectionProvider.cs:32`

`GetGenericDiskCollection` uses a local `_collectionProviderCache` instead of the injected `ICollectionPool`, making it inconsistent with how the rest of the system manages collection instances.

**Task:** Refactor `GetGenericDiskCollection` to use `ICollectionPool` for consistency and to benefit from whatever pooling/lifecycle management it provides.

---

## 4. Conditional cache decision in MongoDbServiceFactory
**File:** `Tharga.MongoDB/Internals/MongoDbServiceFactory.cs:48`

Caching is hardcoded to `true`. The original intent was to conditionally disable caching when a `DatabaseName` fingerprint is present (multi-tenant / per-request databases), but the logic was commented out pending a clean solution.

**Task:** Restore the conditional cache check — disable caching when the database context carries a dynamic `DatabaseName` (e.g. via `ICollectionFingerprint`) so per-tenant databases get fresh connections.

---

## 5. Clean helpers for sub-objects and sub-collections on PersistableEntityBase
**File:** `Tharga.MongoDB/PersistableEntityBase.cs:10`

No built-in helpers to propagate the `NeedsCleaning` / `EndInit` lifecycle to nested objects or embedded collections within an entity.

**Task:** Add virtual `CleanSubObjects()` and `CleanSubCollections()` helpers on `PersistableEntityBase` that recursively call `NeedsCleaning` / `EndInit` on nested `PersistableEntityBase` properties and `IEnumerable<PersistableEntityBase>` fields.
