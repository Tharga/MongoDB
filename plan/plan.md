# Plan: MCP document inspection — raw JSON + schema comparison

## Steps

### Step 1: Audit and design DTOs
- [x] Confirmed `IMongoDbService.GetCollectionAsync<T>(name)` only operates on the configured database; needed an explicit `(databaseName, collectionName)` overload (added)
- [x] Confirmed `CollectionInfo.EntityTypes` is `string[]` (just names); reflect via `ResolveEntityType(CollectionType)` which already walks up to `RepositoryCollectionBase<,>` and returns the entity generic arg
- [x] Sketched DTOs (`DocumentDto`, `DocumentListQuery`, `DocumentListDto`, `SchemaComparisonDto`, `SchemaComparisonField`, `SchemaFieldClassification`)
- [x] Build clean

### Step 2: New `IDatabaseMonitor` methods + DTOs
- [x] Added DTOs in `Tharga.MongoDB/DocumentInspectionDtos.cs`
- [x] Added 3 methods on `IDatabaseMonitor`
- [x] No-op stubs in `DatabaseNullMonitor`
- [x] `NotImplementedException` in `IngestOnlyMonitor` test mock
- [x] Added `IMongoDbService.GetCollectionAsync(string databaseName, string collectionName)` overload + impl in `MongoDbService` (mirrors the existing `GetCollectionsWithMetaAsync(databaseName, ...)` pattern)
- [x] Build clean

### Step 3-5: Implement the 3 methods in `DatabaseMonitor`
- [x] `GetDocumentAsync` — auto-detects id (Guid → ObjectId → string), `Filter.Eq("_id", v)`, `FirstOrDefaultAsync`, returns `DocumentDto` with Extended JSON or null
- [x] `ListDocumentsAsync` — `limit` capped at 200 (default 20), `skip` clamped, `FilterJson` / `SortJson` parsed via `BsonDocument.Parse` (invalid → `FormatException`)
- [x] `CompareSchemaAsync` — top-level field coverage over up to 500 docs (default 50), classifies each field as Full/Partial/EntityOnly/DocumentOnly via `ResolveEntityType`
- [x] All three reject `Registration.NotInCode` collections with a clear error (remote routing deferred)
- [x] Build clean

### Step 6: Wire MCP tools in `MongoDbToolProvider`
- [x] Added 3 tool name constants + 3 arg schemas
- [x] Appended 3 descriptors to `AllTools` with their level tag
- [x] Added 3 entries to `ToolLevels` (all `DataRead`)
- [x] Added 3 cases to `CallToolAsync` switch + 3 private handlers using existing `FindCollectionAsync` for collection lookup
- [x] Build clean

### Step 7: Tests
- [x] Filtering Theory bumped: `Metadata`: 6, `DataRead`: 11, `DataReadWrite`: 12 (was 6/8/9)
- [x] `ToolProvider_ListTools_AtMetadata_OmitsDataReadAndWriteTools` updated to also exclude the 3 new tools
- [x] Defense-in-depth: `mongodb.get_document` at `Metadata` → `IsError` mentioning `DataAccessLevel.DataRead`
- [x] Happy paths: `get_document` Theory at Guid/ObjectId/string ids; `list_documents` default + with filter+sort; `compare_schema` returns shaped fields
- [x] Negatives: `get_document` missing → `IsError`; `list_documents` invalid filter (mocked `FormatException`) → `IsError`; `compare_schema` defaults sample size to 50
- [x] McpProviderTests: 39 passed / 0 failed
- [x] Full suite: 320 passed / 8 skipped / 0 failed on net10

### Step 8: Build verification on all targets
- [x] Build clean on net8 / net9 / net10 (6 warnings, under 50 budget)

### Step 9: README update
- [x] Added the 3 new tools to the "Tools (System scope)" table with `DataRead` tag
- [x] Added "Document inspection" subsection with use cases, Extended-JSON note, top-level-only schema diff caveat, per-tenant DB note, remote-not-supported caveat
- [x] No changelog bump needed — gating already in 2.11.0; this is purely additive

### Step 10: Milestone commit
- [ ] Commit message: `feat: add MCP document inspection (get/list/compare_schema)`

### Step 11: Closure (per shared-instructions § "Closing a feature")
- [ ] Archive `plan/feature.md` → `$DOC_ROOT/Tharga/plans/Toolkit/MongoDB/done/document-inspection.md`
- [ ] Update `$DOC_ROOT/Tharga/plans/Toolkit/MongoDB/planned/README.md` "Done" list with this entry (the feature wasn't in `planned/` since it came from a request — just add it to the recent-done bullets)
- [ ] Mark the PlutusWave request as Done in `$DOC_ROOT/Tharga/Requests.md` (date + summary) and add a follow-up entry instructing PlutusWave to upgrade
- [ ] Delete `plan/` directory from the repo
- [ ] Final commit: `feat: document-inspection complete`

### Step 12: Push + PR
- [ ] User pushes `feature/mcp-document-inspection` to origin
- [ ] Claude opens PR to `Tharga/master`
- [ ] After merge — delete the feature branch (local + remote)

## Notes
- **Tools, not resources** — see `feature.md` § "Why tools, not resources". URI-template support for true resources (`mongodb://document/{id}`) can be added in a later feature without breaking these tool entry points.
- **Reflection on entity types**: prefer `BsonClassMap` if it's already populated for the entity (honors `[BsonElement]` renames and ignored fields). Fall back to plain reflection on public properties.
- **Schema diff scope**: top-level fields only. Nested document drift is a known limitation; a follow-up could add a depth parameter.
- **Cancellation**: all monitor methods take `CancellationToken` to allow MCP-side timeouts to propagate.

## Last session
Plan drafted from PlutusWave request `Requests.md:694–717`. Branch `feature/mcp-document-inspection` created off master (`ef23c67`). Awaiting user confirmation before starting Step 1.
