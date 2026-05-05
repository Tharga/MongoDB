# Tharga.MongoDB.Mcp

Exposes [`Tharga.MongoDB`](https://www.nuget.org/packages/Tharga.MongoDB) monitoring data and admin actions over [MCP (Model Context Protocol)](https://modelcontextprotocol.io). Plugs into [`Tharga.Mcp`](https://www.nuget.org/packages/Tharga.Mcp) so that Claude, Cursor, and other MCP clients can inspect collections, diagnose slow queries, rebuild indexes, and (optionally) read or modify documents — without SSH'ing to a prod box for `mongosh`.

## Install

```
dotnet add package Tharga.MongoDB.Mcp
```

```csharp
builder.Services.AddThargaMcp(mcp => mcp.AddMongoDB());
app.UseThargaMcp();
```

By default only **metadata** is exposed. Opt in to data tools explicitly:

```csharp
builder.Services.AddThargaMcp(mcp => mcp.AddMongoDB(o =>
{
    o.DataAccess = DataAccessLevel.DataRead;       // adds get_document, list_documents, find_duplicates, explain
    // o.DataAccess = DataAccessLevel.DataReadWrite; // also adds clean
}));
```

The provider is registered on the `System` MCP scope, so it only surfaces to system-level callers (e.g. an admin API key) — not to per-team users.

## What's exposed

**Resources**
- `mongodb://collections` — every registered collection with status, indexes, document count
- `mongodb://monitoring` — recent calls, slow queries, latency
- `mongodb://clients` — connected MongoDB driver clients

**Tools (Metadata level — default)**
- `mongodb.touch` — initialise a collection (assure indexes, etc.)
- `mongodb.rebuild_index` — drop & recreate a named index
- `mongodb.drop_index`, `mongodb.reset_cache`, `mongodb.clear_call_history`, `mongodb.compare_schema`

**Tools (DataRead)**
- `mongodb.get_document` — raw BSON-as-JSON for a single document by id (auto-detects ObjectId / Guid / string)
- `mongodb.list_documents` — paged listing with optional filter and sort
- `mongodb.find_duplicates`, `mongodb.explain`

**Tools (DataReadWrite)**
- `mongodb.clean` — apply collection cleaners

## Documentation

Full docs and configuration reference: [github.com/Tharga/MongoDB](https://github.com/Tharga/MongoDB).

[![GitHub repo](https://img.shields.io/github/repo-size/Tharga/MongoDB?style=flat&logo=github&logoColor=red&label=Repo)](https://github.com/Tharga/MongoDB)
