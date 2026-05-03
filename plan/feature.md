# Feature: MCP action parity with Blazor components

## Originating Branch
`feature/mcp-action-parity` (off master, 2026-05-03)

## Source
[`$DOC_ROOT/Tharga/plans/Toolkit/MongoDB/planned/27-mcp-action-parity.md`](../../../../Users/danie/SynologyDrive/Documents/Notes/Tharga/plans/Toolkit/MongoDB/planned/27-mcp-action-parity.md)

The first `Tharga.MongoDB.Mcp` release shipped 3 tools (`mongodb.touch`, `mongodb.rebuild_index`, `mongodb.restore_all_indexes`). The Blazor admin UI exposes more collection- and call-level actions; an AI agent on `/mcp` should be able to do everything the admin UI can do (excluding live-streaming and interactive multi-step dialogs).

In the planning pass, scope grew to also include a **data-access safety gate** so that an admin who installs `Tharga.MongoDB.Mcp` does not, by default, expose document data via MCP.

## Goal
Add 6 new tools to `MongoDbToolProvider`. Introduce a `DataAccessLevel` opt-in for tools/resources that read or write actual document data. Default is metadata-only — including retroactively gating `mongodb://monitoring` (which leaks query filter values via `recentCalls` / `slowCalls`).

## Scope

### New tools
| Tool | Maps to | Level | Notes |
|---|---|---|---|
| `mongodb.drop_index` | `IDatabaseMonitor.DropIndexAsync(CollectionInfo)` | Metadata | Returns `(int Before, int After)` |
| `mongodb.reset_cache` | `IDatabaseMonitor.ResetAsync()` | Metadata | No args |
| `mongodb.clear_call_history` | `IDatabaseMonitor.ResetCalls()` | Metadata | No args |
| `mongodb.find_duplicates` | `IDatabaseMonitor.GetIndexBlockersAsync(CollectionInfo, string indexName)` | DataRead | Returns duplicate-key tuples (= actual data values) |
| `mongodb.explain` | `IDatabaseMonitor.GetExplainAsync(Guid callKey, CancellationToken)` | DataRead | Explain plan typically embeds the query filter values |
| `mongodb.clean` | `IDatabaseMonitor.CleanAsync(CollectionInfo, bool cleanGuids)` | DataReadWrite | Deletes orphaned/invalid documents |

### Data-access gating

Public API:
```csharp
public enum DataAccessLevel
{
    Metadata,       // default — admin + metadata only
    DataRead,       // adds tools/resources that read actual document data
    DataReadWrite,  // adds tools that write/delete data
}

public class MongoDbMcpOptions
{
    public DataAccessLevel DataAccess { get; set; } = DataAccessLevel.Metadata;
}
```

New builder overload:
```csharp
public static IThargaMcpBuilder AddMongoDB(
    this IThargaMcpBuilder builder,
    Action<MongoDbMcpOptions>? configure = null);
```

Filtering:
- `ListToolsAsync` / `ListResourcesAsync` filter out tools/resources that exceed the configured level — agents simply don't see them.
- `CallToolAsync` / `ReadResourceAsync` defensively return `IsError = true` if a gated tool/resource is invoked anyway (defense in depth).

### Retroactive protection
- Existing resource `mongodb://monitoring` is reclassified as **DataRead** because `recentCalls` / `slowCalls` / `slowCallsWithIndex` carry filter shapes that include live data values.
- All other existing surfaces (`mongodb://collections`, `mongodb://clients`, `mongodb.touch`, `mongodb.rebuild_index`, `mongodb.restore_all_indexes`) remain Metadata level — unchanged behavior.
- Default `DataAccess = Metadata` means `mongodb://monitoring` no longer surfaces unless the consumer opts in. **Breaking change vs 2.10.x; bump to 2.11.0.**

## Out of scope (deferred)
- Per-API-key claim-based gating (e.g. `mongodb:read` / `mongodb:read-write`). Requires hooks in `Tharga.Mcp` (claim surface on `IMcpContext`) and `Tharga.Platform.Mcp` (claim emission). Filed as a future request once the platform side is ready; the per-server config is the immediate fallback safety.
- Live streaming (Ongoing calls, Queue live metrics) — MCP `resources/subscribe` semantics.
- Interactive multi-step dialogs (confirm clean, view duplicates inline before drop).
- Optional `mongodb://calls/{callKey}` resource — URI-template support varies; deferred.
- Document inspection feature (#5 in `planned/`) — separate, larger scope; will *consume* the gating mechanism shipped here.

## Acceptance criteria
- [ ] `DataAccessLevel` enum + `MongoDbMcpOptions` + new builder overload public API
- [ ] All 6 new tools implemented in `MongoDbToolProvider` and tagged with their level
- [ ] All tools delegate to `IDatabaseMonitor` (so remote collections route through `IRemoteActionDispatcher` automatically)
- [ ] `mongodb://monitoring` gated behind `DataRead`
- [ ] `ListToolsAsync` / `ListResourcesAsync` only surface what the configured level allows
- [ ] `CallToolAsync` / `ReadResourceAsync` return `IsError = true` for gated entries called by name (defense in depth)
- [ ] Tests: filtering correctness at each level (Metadata / DataRead / DataReadWrite); one happy-path per new tool; negative tests for invalid Guid (`mongodb.explain`) and missing collection
- [ ] `McpProviderTests.ToolProvider_ListTools_ReturnsExpected` updated for level-based counts
- [ ] README MCP section updated: new tools listed with level tag; new "Data access levels" subsection; 2.11.0 note about the default-on retro-gating
- [ ] Build + tests green on net8/9/10 within the 50-warning budget

## Done condition
PR merged into master. An AI agent on `/mcp` can do everything the Blazor admin UI does (minus streaming) when the host opts in to `DataAccessLevel.DataReadWrite`. By default, the package exposes only metadata and admin tools — including retroactively for `mongodb://monitoring`. The pattern set here generalises to the future document-inspection feature (#5).
