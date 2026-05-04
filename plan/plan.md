# Plan: Keyset-paginated `GetPageAsync` / `GetPageProjectionAsync`

## Steps

### Step 1: Audit (already done in pre-plan)
- [x] No stale `GetPageAsync` / `PagePosition` / `CursorPage` references in the codebase
- [x] No existing `Tharga.MongoDB.Paging` namespace — fresh slate
- [x] `GetManyAsync` and `GetManyProjectionAsync` exist in `DiskRepositoryCollectionBase` and stay as the skip-based APIs (orthogonal to the new keyset path)
- [x] All reads funnel through `DiskRepositoryCollectionBase.ExecuteAsync<T>` (already session-aware after `feature/transactions`); the new methods follow the same pattern with `Operation.Read`

### Step 2: Public API surface
- [ ] Create `Tharga.MongoDB/Paging/` folder
- [ ] Add `CursorToken` (readonly struct):
  - Carries: sort-field path (string), sort direction (`Ascending`/`Descending`), sort-field value (object/`BsonValue`), `_id` (object/`BsonValue`)
  - `ToString()` returns Base64URL of a small BSON document `{ f, d, v, i }`
  - `Parse(string)` reverses it; bad input throws `FormatException` with a clear message
  - `Empty` static sentinel
  - Equality / `GetHashCode` based on the encoded string for record-like behavior
- [ ] Add `PagePosition` (readonly struct):
  - Static factories `First`, `Last`, `After(CursorToken cursor, int pageStep = 0)`, `Before(CursorToken cursor, int pageStep = 0)`
  - Internal kind enum (First / Last / After / Before) plus optional `CursorToken` and `PageStep`
  - Validation: `pageStep < 0` throws; `pageSize <= 0` is a separate guard inside `GetPageAsync`
- [ ] Add `CursorPage<T>` (sealed record): `T[] Items, CursorToken FirstCursor, CursorToken LastCursor, bool HasNext, bool HasPrevious`
- [ ] Add abstract `GetPageAsync` / `GetPageProjectionAsync` on `RepositoryCollectionBase<TEntity, TKey>` with the planned signatures
- [ ] Add the same on `IRepositoryCollection<TEntity, TKey>` (interface contract)
- [ ] Stub the implementations on `DiskRepositoryCollectionBase` and `LockableRepositoryCollectionBase` with `NotImplementedException`
- [ ] Build solution; check for ripples on test mocks (none expected; no mocks of `IRepositoryCollection`)

### Step 3: `CursorToken` — encode / decode + sort-mismatch detection
- [ ] Implement BSON-doc round-trip:
  - Encode: `new BsonDocument { ["f"] = path, ["d"] = ascending ? 1 : -1, ["v"] = sortValue, ["i"] = id }.ToBson()` → Base64URL
  - Decode: Base64URL → bytes → `BsonDocument` → extract fields
- [ ] Sort-mismatch detection: when a token is used by `GetPageAsync`, the impl compares `(token.SortFieldPath, token.Direction)` to the call's sort and throws `InvalidOperationException` with a clear message ("This cursor was issued for sort '{x}' direction {asc|desc}; the current call uses sort '{y}' direction {asc|desc}. Cursors are not transferable across sorts.")
- [ ] Unit tests for encode-decode round-trip with `ObjectId`, `DateTime`, `Guid`, `string`, `int`, and a null sort value
- [ ] Unit tests for malformed input → `FormatException`
- [ ] Build clean

### Step 4: Implement `DiskRepositoryCollectionBase.GetPageAsync`
- [ ] Helper: extract the sort-field path from `Expression<Func<TEntity, object>>` using the driver's `RenderArgs<TEntity>`-based path so `[BsonElement("foo")]` renames are honored. Match what `Builders<TEntity>.IndexKeys.Ascending(sortBy).Render(...)` produces.
- [ ] Helper: build the cursor filter for `After`/`Before`:
  - Unique sort (sortBy == null → `_id` only): `{_id: {$gt|$lt: cursor.id}}`
  - Non-unique sort: `$or:[{sortField: {$gt|$lt: v}}, {sortField: {$eq: v}, _id: {$gt|$lt: id}}]`
- [ ] Helper: build the sort spec `{sortField: 1, _id: 1}` for ascending; flip both for descending; for unique-only path use just `{_id: 1}`
- [ ] Build the final filter: caller `predicate` AND'd with the cursor filter (if any)
- [ ] Per-position handling:
  - `First`: no cursor filter, sort per `ascending`, `Limit(pageSize)`
  - `Last`: no cursor filter, **flip** the sort direction, `Limit(pageSize)`, **reverse items in memory**
  - `After(cursor, pageStep)`: validate sort match, cursor `>` filter, sort per `ascending`, `Skip(pageStep × pageSize).Limit(pageSize)`
  - `Before(cursor, pageStep)`: validate sort match, cursor `<` filter, **flip** sort, `Skip(pageStep × pageSize).Limit(pageSize)`, **reverse items in memory**
- [ ] After fetching, build `CursorPage<TEntity>`:
  - `Items` — final ordered list (post-reverse if applicable)
  - `FirstCursor` — token for `Items[0]` (or `CursorToken.Empty` if items empty)
  - `LastCursor` — token for `Items[^1]`
  - `HasNext` — true iff there's at least one matching doc strictly past `LastCursor` (cheap: probe `Find(predicate ∧ keysetAfterLastCursor).Limit(1).Any()`; or check whether the fetch returned `pageSize` items AND we know more exist by an extra count… simpler: do the explicit probe)
  - `HasPrevious` — same shape, in the other direction from `FirstCursor`
- [ ] Use `ExecuteAsync(nameof(GetPageAsync), ..., Operation.Read, filter: combinedFilter)` so the call shows up in admin-UI tracking
- [ ] Argument validation: `pageSize > 0`, `pageStep >= 0`, `position` non-default
- [ ] Build clean

### Step 5: Implement `GetPageProjectionAsync`
- [ ] Mirror Step 4's implementation but materialize a projected `T[]` instead of `TEntity[]`
- [ ] Cursor tokens are still computed from the **entity's** sort-field + `_id` — so the projection stage runs after the sort-and-cursor stage. Internally: fetch raw `TEntity` for boundary documents, then project; or do a server-side `$project` and ensure the projected shape still includes the boundary fields. **Decision: use a two-step approach** — server-side find returns full `TEntity`, then we materialize the projected `T` via `projection.Compile()` client-side. Reason: keeps cursor-token construction trivial and avoids a separate "include _id and sortField in projection" wart on the API.
- [ ] If profiling later shows the client-side projection is too costly for very wide entities, we can switch to a server-side `$project` that explicitly preserves `{_id, sortField, ...projectedFields}` — but defer that until a consumer measures the cost.
- [ ] Build clean

### Step 6: Lockable delegation
- [ ] Override `GetPageAsync` / `GetPageProjectionAsync` on `LockableRepositoryCollectionBase<TEntity, TKey>` with one-line delegation to `Disk.GetPageAsync(...)` / `Disk.GetPageProjectionAsync(...)` — matches existing read-method delegation pattern
- [ ] Build clean

### Step 7: Tests
- [ ] New `Paging/CursorTokenTests.cs` — pure unit tests, no DB:
  - Round-trip for `ObjectId`, `DateTime` (UTC + offsets), `Guid`, `string`, `int`, `long`, `decimal`, `null`
  - Malformed Base64URL → `FormatException`
  - Truncated BSON → `FormatException`
  - `Empty` round-trip
- [ ] New `Paging/GetPageAsyncTests.cs` — DB-backed (existing `LockableTestBase`-style fixture):
  - Seed 50 docs; First page returns first 10 in ascending order; `HasNext=true, HasPrevious=false`
  - Next page from `LastCursor` returns next 10; `HasNext=true, HasPrevious=true`
  - Previous page from `FirstCursor` of page 2 returns page 1
  - Last page returns the final partial-or-full page in correct ascending order; `HasNext=false`
  - `pageStep:1` jump skips one page
  - Sort by `_id` ascending — the unique-only path
  - Sort by `_id` descending — flip path
  - Sort by a non-unique field (e.g. `Count` with several duplicates) — verify tiebreaker by `_id`
  - Predicate filter ANDed with cursor: filter `Count > 5`, page through; only matching docs appear
  - Empty collection: First / Last / After(empty cursor) all return empty `CursorPage` with both flags false
  - Projection variant: returns projected DTO shape
  - Cursor from sort A used with sort B → `InvalidOperationException` with a clear message
  - Lockable collection delegates correctly: same scenario produces same results when called on a `LockableTestRepositoryCollection`
- [ ] Run McpProviderTests + full suite — green on net10 (1 known-flaky `GetLockedExpired` allowed)

### Step 8: Index-plan verification test
- [ ] Add an explain-plan check (use `Disk.ExecuteAsync(...)` to run `explain` on a representative page query against a seeded collection that has the recommended `{sortField, _id}` index) — assert the winning plan stage is `IXSCAN`, not `COLLSCAN`
- [ ] Note: this is a guidance test, not an enforcement; running without the index still works, just slower

### Step 9: Build verification on all targets
- [ ] Build on net8 / net9 / net10 — clean, warnings under 50 budget

### Step 10: README update
- [ ] Extend the "Custom queries" section with a new "Keyset pagination" subsection covering:
  - Why (skip penalty on deep pages, "jump to last")
  - Basic usage — `GetPageAsync(pageSize, PagePosition.First, ...)`, then chaining via `LastCursor`/`FirstCursor`
  - Sort + filter combinator example
  - Index guidance: compound `{sortField, _id}` per sortable column
  - Total count: separate cached `CountAsync(predicate)` call
  - Radzen integration pattern: how to map `LoadDataEventArgs.Skip` to `PagePosition` and how `PagingSummaryFormat` works without an in-page count

### Step 11: Milestone commit + closure
- [ ] (Per-step commits along the way — this is the closure)
- [ ] Archive `plan/feature.md` → `$DOC_ROOT/Tharga/plans/Toolkit/MongoDB/done/keyset-pagination.md`
- [ ] Delete `$DOC_ROOT/Tharga/plans/Toolkit/MongoDB/planned/33-keyset-pagination.md`
- [ ] Update `$DOC_ROOT/Tharga/plans/Toolkit/MongoDB/planned/README.md`: drop #33 row; add to "Done" recent list. After this lands the planned/ directory is empty (only `README.md` remains) — note that explicitly so future planners know to look at cross-project `Requests.md` for the next slot
- [ ] Delete `plan/` directory from the repo
- [ ] Final commit: `feat: keyset-pagination complete`

### Step 12: Push + PR
- [ ] User pushes `feature/keyset-pagination` to origin
- [ ] Claude opens PR to `Tharga/master`
- [ ] After merge — delete the feature branch (local + remote)

## Notes

### Risk: sort-field path extraction
The driver's expression-to-field-path machinery isn't always intuitive. We need to use the **same** path that `Builders<TEntity>.IndexKeys.Ascending(expr).Render(...)` and `Builders<TEntity>.Filter.Eq(expr, value).Render(...)` produce — that's how the cursor filter and the `IXSCAN` line up correctly. Plan to validate by rendering both and asserting they match in the test that checks the explain plan.

### Risk: client-side projection (`GetPageProjectionAsync`)
Compiling `Expression<Func<TEntity, T>>` and applying it client-side (Step 5 default) means the entire entity comes off the wire. For wide entities with many large fields and a narrow projection, that's wasteful. Decision: ship the simple version; if a consumer measures it as a problem we can switch to a server-side `$project` that preserves the boundary fields. Recorded as a follow-up in `feature.md`.

### Risk: `HasNext` / `HasPrevious` cost
Naive impl probes the next-or-prev document with a separate `Find(...).Limit(1).Any()`. That's two queries per page-fetch instead of one. Acceptable: both queries hit the same index, the second is bounded to one document. Don't overengineer.

### Closes
- **planned/33** — only remaining queued planned feature in `Toolkit/MongoDB`. Once shipped, the planned/ directory is essentially empty and the next slot comes from cross-project `Requests.md`.

## Last session
Plan drafted from `planned/33-keyset-pagination.md`. Branch `feature/keyset-pagination` created off master (`98caa56`). Pre-plan audit confirmed: no stale `GetPageAsync` references; no existing `Tharga.MongoDB.Paging` namespace; reads funnel through `DiskRepositoryCollectionBase.ExecuteAsync<T>` which is already session-aware. Awaiting user confirmation before starting Step 2.
