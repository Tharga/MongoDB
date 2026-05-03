# Feature: MCP document inspection — raw JSON + schema comparison

## Originating Branch
`feature/mcp-document-inspection` (off master, 2026-05-03)

## Source
[`$DOC_ROOT/Tharga/Requests.md` lines 694–717](../../../../Users/danie/SynologyDrive/Documents/Notes/Tharga/Requests.md), from PlutusWave 2026-04-20, **High** priority.

> *"`Tharga.MongoDB.Mcp 2.10.4` exposes metadata but nothing at the document level. When investigating schema drift or migration fallout (e.g. team-member documents where some have `EMail`, some `Name`, some neither — as seen in PlutusWave prod after the Platform migration), consumers need to see the raw document as MongoDB stored it, before the C# serializer maps it. Today the only way is to SSH into the server and run `mongosh` — often blocked in prod."*

## Goal
Let an authorized agent inspect raw documents and detect schema drift via MCP — the same diagnostic loop today done via `mongosh` on a production shell, available through the existing MCP plumbing instead.

## Scope

### New tools — all at `DataAccessLevel.DataRead`

| Tool | Args | Returns |
|---|---|---|
| `mongodb.get_document` | `configurationName?`, `databaseName`, `collectionName`, `id` | Single document as MongoDB Extended JSON (raw BSON, no C# serializer in the path). `id` is auto-detected as Guid → ObjectId → string. |
| `mongodb.list_documents` | `configurationName?`, `databaseName`, `collectionName`, `limit?` (default 20, capped at 200), `skip?`, `filter?` (JSON string), `sort?` (JSON string `{"field": 1}`) | Up to `limit` documents as Extended JSON. `filter` parsed via `BsonDocument.Parse`. |
| `mongodb.compare_schema` | `configurationName?`, `databaseName`, `collectionName`, `sampleSize?` (default 50, capped at 500) | Three-way diff: (1) C# entity type's public properties, (2) registered entity-type name(s) from `CollectionInfo`, (3) fields present in sampled docs (set union + "appears in N/M" coverage). Flags entity-only, doc-only, and partial-coverage fields. |

### Why tools, not resources
The PlutusWave request shows them as `mongodb://document/{...}` and `mongodb://documents/{...}` URIs. In practice the operations take parameters (id, limit, skip, filter, sort) — which is what MCP **tools** are for. **Resources** in MCP/Tharga.Mcp are typically static URIs surfaced via `resources/list`. Going with tools keeps the implementation aligned with existing patterns (`mongodb.touch`, `mongodb.find_duplicates`, etc.) and avoids URI-template machinery that Tharga.Mcp 0.1.x doesn't expose. We can revisit with template resources later without breaking these tool entry points.

### Dynamic / partitioned DB support
The existing `mongodb://collections` resource already lists per-tenant databases (e.g. `PlutusWave_Production_<teamKey>`) by their resolved `databaseName`. The new tools accept `databaseName` directly, so per-tenant collections are supported without a special "part" parameter.

### Plumbing — `IDatabaseMonitor`
Three new methods to keep the MCP provider thin and route remote collections through the existing `IRemoteActionDispatcher`:

```csharp
Task<DocumentDto> GetDocumentAsync(CollectionInfo info, string idRaw, CancellationToken ct);
Task<DocumentListDto> ListDocumentsAsync(CollectionInfo info, DocumentListQuery query, CancellationToken ct);
Task<SchemaComparisonDto> CompareSchemaAsync(CollectionInfo info, int sampleSize, CancellationToken ct);
```

Where:
- `DocumentDto` = `{ string Id, string Json }` (Extended JSON; no C# entity in the contract).
- `DocumentListQuery` = `{ int Limit, int Skip, string FilterJson, string SortJson }`.
- `DocumentListDto` = `{ DocumentDto[] Documents, int TotalReturned, bool Truncated }`.
- `SchemaComparisonDto` = full diff structure (entity properties, registered entity names, sampled field coverage, classification per field).

Implementations:
- `DatabaseMonitor` — actual logic against the underlying `IMongoCollection<BsonDocument>`.
- `DatabaseNullMonitor` — no-op (throws or returns empty).
- `IngestOnlyMonitor` test mock — `NotImplementedException` (consistent with existing pattern).

## Out of scope
- Per-API-key claim-based gating (still deferred to a future Tharga.Mcp + Tharga.Platform.Mcp coordination).
- Mutation tools (insert/update/delete documents). Read-only feature, by design.
- Streaming / paginated cursor across calls. Single response, capped by `limit`.
- BSON-to-poco mapping (e.g. "show me how this doc would deserialize"). Out of scope; the point is the *raw* shape.
- Full-text or aggregation pipeline support beyond `find` + `filter` + `sort`.

## Acceptance criteria
- [ ] Three new tool entries in `MongoDbToolProvider`, all tagged `DataRead`
- [ ] Three new methods on `IDatabaseMonitor` with concrete implementations + null/test mocks updated
- [ ] `mongodb.get_document` auto-detects id format (Guid → ObjectId → string) and returns Extended JSON; missing doc → `IsError`
- [ ] `mongodb.list_documents` respects `limit` (default 20, max 200) and parses `filter` / `sort` as BsonDocument; invalid JSON → `IsError`
- [ ] `mongodb.compare_schema` returns the three-way diff with per-field coverage counts; reflection over the entity type uses the existing `CollectionInfo.EntityTypes`
- [ ] Per-tenant DBs (`DatabasePart` style) work directly via the resolved `databaseName`
- [ ] All three tools surface only at `DataAccessLevel.DataRead` or higher (filter test); call at `Metadata` returns the level error
- [ ] Tests cover: happy path per tool; id-format detection; filter/sort parsing; limit cap; missing doc; missing collection; schema-comparison shape with a synthetic entity
- [ ] README MCP section updated: 3 new tools in the table, brief subsection on document inspection use cases
- [ ] Build + tests green on net8 / net9 / net10 within the 50-warning budget

## Done condition
PR merged into master. PlutusWave (or any authorized agent) can fetch a raw document, list raw documents with a filter, and produce a schema-drift diagnosis via MCP — without SSHing onto a prod box. The package's data-access gating ensures these are off by default.
