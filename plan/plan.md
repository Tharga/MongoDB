# Plan: MCP action parity with Blazor components

## Steps

### Step 1: Public API for the gating
- [x] Add `DataAccessLevel` enum (`Metadata`, `DataRead`, `DataReadWrite`) in `Tharga.MongoDB.Mcp`
- [x] Add `MongoDbMcpOptions` with `DataAccess` (default `Metadata`)
- [x] Add overload `IThargaMcpBuilder AddMongoDB(this IThargaMcpBuilder, Action<MongoDbMcpOptions> configure = null)` — source-compat with the old parameterless call site
- [x] Register `MongoDbMcpOptions` as a singleton so the providers can resolve it
- [x] Build solution — clean, 0 warnings

### Step 2: Filtering helper + tag existing surface
- [x] Tagged via `IReadOnlyDictionary<string, DataAccessLevel>` per provider; `required <= current` check
- [x] Tagged the 3 existing tools (`touch`, `rebuild_index`, `restore_all_indexes`) as `Metadata`
- [x] Tagged `mongodb://collections` and `mongodb://clients` as `Metadata`
- [x] Tagged `mongodb://monitoring` as `DataRead` (retroactive protection)
- [x] `MongoDbToolProvider.ListToolsAsync` / `MongoDbResourceProvider.ListResourcesAsync` filter by configured level
- [x] `CallToolAsync` returns `IsError = true` if a gated tool is invoked at a lower level; `ReadResourceAsync` returns a level-message text content
- [x] Build solution — clean

### Step 3: Implement the 6 new tools
- [x] Added 6 tool-name constants and per-tool `JsonElement` argument schemas (`CollectionArgSchema` reused for `drop_index`; `CleanArgSchema` adds `cleanGuids`; `FindDuplicatesArgSchema` adds `indexName`; `ExplainArgSchema` for `callKey`; `EmptyArgSchema` for `reset_cache` / `clear_call_history`)
- [x] Static `AllTools` array now declares all 9 with level tags + descriptions
- [x] `CallToolAsync` switch covers all 9 cases dispatching to private handlers
- [x] Handlers implemented; `mongodb.explain` parses `callKey` as `Guid` with explicit error on invalid input
- [x] Build solution — clean

### Step 4: Tests
- [x] Filtering tests in `McpProviderTests` (Theory at each level, asserts URI list / tool count)
- [x] Defense-in-depth tests: `mongodb.find_duplicates` at `Metadata` and `mongodb.clean` at `DataRead` both return `IsError` mentioning the required level; `mongodb://monitoring` read at `Metadata` returns level-message text
- [x] Happy-path tests for `drop_index`, `reset_cache`, `clear_call_history`, `find_duplicates`, `explain`, `clean` (with and without `cleanGuids`)
- [x] Negative tests: `mongodb.explain` with `"not-a-guid"` returns `IsError`; existing collection-not-found test still passes
- [x] `ToolProvider_ListTools_FiltersByAccessLevel` Theory replaces the old fixed-count test
- [x] McpProviderTests: 29 passed / 0 failed; full suite 309 passed / 1 known flaky (lockable timing) / 8 skipped

### Step 5: Build verification on all targets
- [x] Build on net8 / net9 / net10 — clean, 4 warnings (under 50 budget)

### Step 6: README update
- [x] Added all 6 new tools to the "Tools (System scope)" table with level tags
- [x] Added "Data access levels" subsection with opt-in syntax + DataRead/DataReadWrite examples
- [x] Added 2.10.x → 2.11.0 upgrade note about default-on gating of `mongodb://monitoring`

### Step 7: Milestone commit
- [ ] Commit message: `feat: add data-access gating + 6 MCP action tools`

### Step 8: Closure (per shared-instructions § "Closing a feature")
- [ ] Archive `plan/feature.md` → `$DOC_ROOT/Tharga/plans/Toolkit/MongoDB/done/mcp-action-parity.md`
- [ ] Delete `$DOC_ROOT/Tharga/plans/Toolkit/MongoDB/planned/27-mcp-action-parity.md` (now superseded by the archived version in `done/`)
- [ ] Update `$DOC_ROOT/Tharga/plans/Toolkit/MongoDB/planned/README.md` to drop the #27 row
- [ ] Delete `plan/` directory from the repo
- [ ] Final commit: `feat: mcp-action-parity complete`

### Step 9: Push + PR
- [ ] User pushes `feature/mcp-action-parity` to origin
- [ ] Claude opens PR to `Tharga/master`
- [ ] After merge — delete the feature branch (local + remote)

## Notes
- All 6 target methods on `IDatabaseMonitor` already exist with the expected signatures.
- The gating is the source of breaking change; the action parity itself is purely additive. Keep them in one PR because the new tools are introduced alongside their level tagging — splitting them would mean shipping `clean` / `find_duplicates` / `explain` ungated even briefly.
- Per-API-key claim-based gating is deliberately deferred — see `feature.md` "Out of scope" for rationale.
