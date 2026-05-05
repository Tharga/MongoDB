# MCP integration

The [`Tharga.MongoDB.Mcp`](https://www.nuget.org/packages/Tharga.MongoDB.Mcp) package exposes MongoDB monitoring data and admin actions over [MCP (Model Context Protocol)](https://modelcontextprotocol.io). AI agents (Claude, Cursor, etc.) can query collections, inspect monitoring data, and trigger admin actions — without SSH-ing to a prod box for `mongosh`.

## Install + register

```
dotnet add package Tharga.MongoDB.Mcp
```

```csharp
services.AddThargaMcp(mcp =>
{
    mcp.AddMongoDB();
});

app.UseThargaMcp();
```

The provider is registered on `McpScope.System` and surfaces only to system-level MCP callers.

## Data access levels

Default exposure is **metadata only** — nothing that returns or modifies actual document data. Opt in for more:

```csharp
services.AddThargaMcp(mcp =>
{
    mcp.AddMongoDB(o =>
    {
        o.DataAccess = DataAccessLevel.DataRead;       // adds tools/resources that read document data
        // o.DataAccess = DataAccessLevel.DataReadWrite; // also adds tools that modify data (e.g. mongodb.clean)
    });
});
```

Anything above the configured level is filtered out of `tools/list` and `resources/list`, and rejected at `tools/call` / `resources/read` with an `IsError` response.

## Resources

| URI | Level | Description |
|---|---|---|
| `mongodb://collections` | Metadata | Collections with stats, indexes, clean status |
| `mongodb://clients` | Metadata | Connected remote monitoring agents |
| `mongodb://monitoring` | DataRead | Recent + slow calls, summaries, error summary, connection-pool state (calls embed filter values, hence the gating) |

## Tools

| Tool | Level | Args |
|---|---|---|
| `mongodb.touch` | Metadata | `databaseName`, `collectionName`, optional `configurationName` |
| `mongodb.rebuild_index` | Metadata | `databaseName`, `collectionName`, optional `configurationName`, `force` |
| `mongodb.restore_all_indexes` | Metadata | optional `configurationName` / `databaseName` filters |
| `mongodb.drop_index` | Metadata | drops indexes not declared in code |
| `mongodb.reset_cache` | Metadata | resets the in-memory monitor cache |
| `mongodb.clear_call_history` | Metadata | clears recent + slow call history |
| `mongodb.find_duplicates` | DataRead | `databaseName`, `collectionName`, `indexName`; returns duplicate-key tuples |
| `mongodb.explain` | DataRead | `callKey` (Guid); returns explain plan + original filter |
| `mongodb.clean` | DataReadWrite | deletes orphaned / invalid documents |
| `mongodb.get_document` | DataRead | `id` is auto-detected as Guid → ObjectId → string; returns MongoDB Extended JSON |
| `mongodb.list_documents` | DataRead | `limit` (default 20, max 200), `skip`, JSON `filter`, JSON `sort` |
| `mongodb.compare_schema` | DataRead | three-way diff: C# entity properties vs registered type names vs field set observed in sample (default 50 docs, max 500) |

## Document inspection

`mongodb.get_document`, `mongodb.list_documents`, and `mongodb.compare_schema` let an authorized agent diagnose schema drift and migration fallout via MCP — the same loop typically done via `mongosh` on a production shell, but through the MCP plumbing you already use for everything else.

- All three are `DataRead` — hidden by default. Opt in via `o.DataAccess = DataAccessLevel.DataRead`.
- Documents are returned as MongoDB Extended JSON — the exact shape stored in MongoDB, never round-tripped through the C# serializer.
- `compare_schema` reflects on the entity type's public properties and compares against the sampled field set. Top-level fields only — nested-document drift is a known limitation.
- Per-tenant databases (`DatabasePart` / per-team DBs) work directly: pass the resolved `databaseName` from `mongodb://collections`.
- Remote-only collections (`Registration.NotInCode`) are not yet supported — these tools throw a clear error. Adding remote routing requires extending `IRemoteActionDispatcher` and the Monitor.Server pipeline; planned as a follow-up.

## See also

- [API: MongoDbMcpOptions](xref:Tharga.MongoDB.Mcp.MongoDbMcpOptions)
- [API: DataAccessLevel](xref:Tharga.MongoDB.Mcp.DataAccessLevel)
- [Monitoring](monitoring.md) — the data the MCP surface exposes
