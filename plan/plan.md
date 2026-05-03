# Plan: Fix BySchema index assurance bug + Lockable index verification

## Steps

### Step 1: Reproducing tests for the BySchema bug
- [x] Add unit test: collection with both `CoreIndices` (e.g. lockable) and consumer `Indices` — `BySchema` mode creates the lockable indexes when none exist locally
- [x] Add unit test: same shape, but consumer `Indices` already exists — `BySchema` still creates the missing lockable indexes (currently fails because of `Zip` mispairing)
- [x] Add unit test: `BySchema` matches `ByName` for the same defined index set
- [x] Run — confirm failures match the suspected bug
- [x] Build and verify

### Step 2: Fix `UpdateIndicesBySchemaAsync`
- [x] Replace `Zip` pairing with a lookup: for each defined `IndexMeta` not present in `existingIndiceModel`, find the corresponding `CreateIndexModel` by schema match (rendered keys + uniqueness)
- [x] Run regression tests — confirm they now pass
- [x] Build and verify

### Step 3: Fix `BuildIndexMetas` ordering
- [x] Change `indices.Union(coreIndices)` → `coreIndices.Union(indices)` so order matches every other call site
- [x] Also fixed reflection bug: `BindingFlags.NonPublic | Instance` doesn't find non-public members on a base class — walk the inheritance chain manually with `BindingFlags.DeclaredOnly`
- [x] Also fixed test infra (`MongoDbTestBase`): replaced Loose `IInitiationLibrary` mock with the real `InitiationLibrary` so `AssureIndex` actually runs (the Loose mock was returning `false` from `ShouldInitiate`/`ShouldInitiateIndex`, silently bypassing index creation in every test)
- [x] Build and run all tests — 4/4 BySchema tests now pass; full suite passes (one unrelated timing-flaky lockable test passed in isolation)

### Step 4: Add validation to `BySchema` and `DropCreate`
- [x] Add duplicate-name check up front to `UpdateIndicesBySchemaAsync` (mirrors `ByName`)
- [x] Add duplicate-name check up front to `UpdateIndicesByDropCreateAsync` (mirrors `ByName`)
- [x] Add duplicate-schema warning log to `UpdateIndicesBySchemaAsync` (two defined indexes with identical fields + uniqueness)
- [x] Tests for the validation (`BySchema_DuplicateIndexNames_Throws`, `DropCreate_DuplicateIndexNames_Throws`)
- [x] Build and verify

### Step 5: Verification test for lockable
- [x] Added `LockableCoreIndicesShapeTest` with two assertions: single-key `{Lock: 1}` index and compound `{Lock.ExceptionInfo, Lock.ExpireTime, Lock.LockTime}` index — fails build if shapes drift
- [x] Build and verify

### Step 6: Add `RestoreAllIndicesAsync` to `IDatabaseMonitor`
- [x] Defined `IndexAssureProgress` and `IndexAssureSummary` records (single file `IndexAssureProgress.cs`)
- [x] Added interface method with `Func<CollectionInfo, bool>` filter, `IProgress<IndexAssureProgress>` reporter, `CancellationToken`
- [x] Implemented in `DatabaseMonitor` — collects instances, skips `Registration.NotInCode`, catches per-collection exceptions, reports progress, returns summary
- [x] No-op in `DatabaseNullMonitor` returns empty summary
- [x] Updated `IngestOnlyMonitor` test mock with `NotImplementedException`
- [x] Build and verify

### Step 7: Wire into Blazor `MonitorToolbar`
- [x] Added "Assure all indices" menu item with `build_circle` icon and tooltip
- [x] Wired notification per progress event (Success / Info for skipped / Error) plus final summary notification
- [x] Build and verify

### Step 8: MCP exposure
- [x] Added `mongodb.restore_all_indexes` tool with optional `configurationName` and `databaseName` filters
- [x] Updated `McpProviderTests.ToolProvider_ListTools_ReturnsExpected` count from 2 to 3
- [x] Build and verify

### Step 9: README and commit
- [x] Added "Index assurance modes" section with reconciliation table and caveats
- [x] Added "Re-applying indexes after a code change" section linking helper / Blazor action / MCP tool
- [x] Updated MCP tools list with `mongodb.restore_all_indexes`
- [x] Final build clean, all 294 tests pass (8 skipped, 0 failed)
- [ ] Commit all changes (awaiting user direction)

## Last session

All implementation complete. Tests green: 294/294 passed on net10. Build clean across net8/9/10 for the library. Awaiting user direction to commit/push/PR.
