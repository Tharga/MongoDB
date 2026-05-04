# Plan: Keyset-paginated `GetPageAsync` / `GetPageProjectionAsync`

## Steps

### Step 1: Audit (already done in pre-plan)
- [x] No stale `GetPageAsync` / `PagePosition` / `CursorPage` references in the codebase
- [x] No existing `Tharga.MongoDB.Paging` namespace — fresh slate
- [x] `GetManyAsync` and `GetManyProjectionAsync` exist in `DiskRepositoryCollectionBase` and stay as the skip-based APIs (orthogonal to the new keyset path)
- [x] All reads funnel through `DiskRepositoryCollectionBase.ExecuteAsync<T>` (already session-aware after `feature/transactions`); the new methods follow the same pattern with `Operation.Read`

### Step 2: Public API surface — DONE
- [x] Created `Tharga.MongoDB/Paging/` folder with `CursorToken`, `PagePosition`, `CursorPage<T>`, `CursorPager<TEntity, TKey>`. All have skeletons that throw `NotImplementedException` for the parts that need real bodies (Steps 3, 7).
- [x] `CursorToken` is a readonly struct with `IsEmpty`, `Empty`, equality + hash based on the encoded string, `ToString` / `Parse` / `From` signatures stubbed.
- [x] `PagePosition` has factories for `First`, `Last`, `After(cursor, pageStep)`, `Before(cursor, pageStep)`. `pageStep` validated `>= 0`.
- [x] `CursorPage<T>` is a sealed record with the four expected members.
- [x] Added abstract `GetPageAsync` / `GetPageProjectionAsync` on `RepositoryCollectionBase<TEntity, TKey>` and the matching contract on `IRepositoryCollection<TEntity, TKey>`. Stubbed `NotImplementedException` on `DiskRepositoryCollectionBase` and a one-line `Disk.GetPageAsync(...)` delegation on `LockableRepositoryCollectionBase` (saves the override needing a stub later).
- [x] Build clean: 16 warnings (all pre-existing); full regression: 340 passed / 8 skipped / 0 failed — same as master baseline.

### Step 3: `CursorToken` — encode / decode + sort-mismatch detection + `From(entity, ...)` — DONE
- [x] BSON-doc round-trip via `new BsonDocument { f, d, v, i }.ToBson()` → Base64URL (manual, RFC 4648 §5: `+ → -`, `/ → _`, drop trailing `=`).
- [x] `CursorToken.From<TEntity, TKey>(entity, sortBy, ascending)`:
  - Sort-field path resolved via `new ExpressionFieldDefinition<TEntity, object>(sortBy).Render(args).FieldName` — uses the same machinery `Builders<T>.IndexKeys.Ascending(expr)` uses, so `[BsonElement]` renames are honored.
  - Boundary value read by compiling the expression (acceptable cost for the fallback path; not on the hot keyset path).
  - `sortBy == null` → `_id`-only token (path `"_id"`, sort value = the entity's id).
- [x] `ValidateForSort(expectedPath, expectedAscending)` internal method on `CursorToken`. Used by `GetPageAsync` impl in Step 4. Throws `InvalidOperationException` with a clear message; `Empty` cursor passes through silently (used at the start of a session).
- [x] **23 unit tests** in `Paging/CursorTokenTests.cs`:
  - Round-trip across `ObjectId`, `DateTime` (UTC), `Guid` (binary), `string`, `int`, `long`, `decimal`, null
  - Both directions (ascending + descending)
  - Empty token round-trip
  - Malformed input: invalid Base64URL chars; valid Base64URL but not BSON; BSON missing required fields; BSON with invalid direction value
  - `ValidateForSort` accepts same-sort, rejects different-field and different-direction, treats Empty as valid
  - `From(entity, sortBy, ascending)` round-trips for null-sortBy (`_id` only), string field, int field
  - `From` rejects null entity
  - Composite test: `From(entity, ...)` → `ToString()` → `Parse(...)` decodes back to identical token
- [x] Full regression: 363 passed / 8 skipped / 0 failed (was 340 baseline + 23 new). Build clean.

### Step 4: Implement `DiskRepositoryCollectionBase.GetPageAsync` — DONE
- [x] Helper `ResolveSortFieldPath` — extracts the sort-field path from `Expression<Func<TEntity, object>>` using the driver's `RenderArgs<TEntity>` machinery so `[BsonElement("foo")]` renames are honored. Same path as `Builders<TEntity>.IndexKeys.Ascending(expr).Render(...)`.
- [x] Helper `BuildCursorFilter` — builds the cursor filter for `After`/`Before`:
  - Unique sort (sortBy == null → `_id` only): `{_id: {$gt|$lt: cursor.id}}`
  - Non-unique sort: `$or:[{sortField: {$gt|$lt: v}}, {sortField: {$eq: v}, _id: {$gt|$lt: id}}]`
  - `goForward == ascending` decides `$gt` vs `$lt`
- [x] Helper `BuildSortDefinition` — `{sortField: 1, _id: 1}` ascending; flipped descending; unique-only path uses just `{_id: ±1}`
- [x] Helper `ComposeFilter` — caller `predicate` AND'd with the cursor filter (handles null on either side)
- [x] Per-position handling implemented in `FetchPageAsync`:
  - `First`: no cursor filter, sort per `ascending`, `Limit(pageSize)`
  - `Last`: no cursor filter, **flips** the sort direction, `Limit(pageSize)`, **reverses items in memory**
  - `After(cursor, pageStep)`: validates sort match via `cursor.ValidateForSort`, cursor `>` filter, sort per `ascending`, `Skip(pageStep × pageSize).Limit(pageSize)`
  - `Before(cursor, pageStep)`: validates sort match, cursor `<` filter, **flips** sort, `Skip(pageStep × pageSize).Limit(pageSize)`, **reverses items in memory**
- [x] Builds `CursorPage<TEntity>` with `Items`, `FirstCursor` (= `CursorToken.From(items[0], sortBy, ascending)`), `LastCursor`, `HasNext`, `HasPrevious`. Empty result → both flags false.
- [x] `HasNext` / `HasPrevious` probed via `Find(predicate ∧ cursorFilter).Limit(1).AnyAsync()` (cheap, hits the same index, bounded).
- [x] Routed through `ExecuteAsync(nameof(GetPageAsync), ..., Operation.Read, filter: predicateFilter)` so the call shows up in admin-UI tracking
- [x] Argument validation: `pageSize > 0` (throws `ArgumentOutOfRangeException`)
- [x] `GetPageProjectionAsync` reuses `FetchPageAsync` then materializes the projected `T[]` via `projection.Compile()` client-side (decision per Step 5 design note)
- [x] Build clean across net8/9/10; full regression: 362 passed / 8 skipped / 6 pre-existing failures (replica-set transaction tests + the known `GetLockedExpired` flake) — same as master baseline

### Step 5: Implement `GetPageProjectionAsync` — DONE (folded into Step 4)
- [x] Done as part of Step 4: reuses `FetchPageAsync` then `projection.Compile()` client-side per the Step-4 decision
- [x] Follow-up captured in `feature.md`: switch to server-side `$project` preserving `{_id, sortField, ...projectedFields}` if profiling later shows client-side is too costly for wide entities

### Step 6: Lockable delegation — DONE (folded into Step 2)
- [x] `LockableRepositoryCollectionBase` overrides for `GetPageAsync` / `GetPageProjectionAsync` already in place from Step 2 stub — one-line delegation to `_disk.GetPageAsync(...)` / `_disk.GetPageProjectionAsync(...)`. No further work needed.

### Step 7: Implement `CursorPager<TEntity, TKey>` — DONE
- [x] Constructor stores the `IRepositoryCollection<TEntity, TKey>` reference
- [x] State fields: `_firstCursor`, `_lastCursor` (`CursorToken.Empty` initially), `_previousSkip = -1`, `_previousPageSize = -1`, `_queryCacheKey` (string), `_totalCount` (long)
- [x] `LoadAsync(skip, pageSize, predicate, sortBy, ascending, ct)` body:
  - Builds a stable cache key for `(predicate, sortBy, ascending)` via `Expression.ToString()` concatenation. Any change → invalidates count + cursors.
  - If the cache key changed → runs `_repository.CountAsync(predicate)`; caches new key + new count + clears cursors + resets `_previousSkip`/`_previousPageSize`.
  - Decodes `(skip, pageSize)` to a `PagePosition`:
    - `skip == 0` → `First`
    - `skip == lastPageSkip` AND `pageSize == _previousPageSize` → `Last` (only when the request lines up with the trailing-partial-page boundary)
    - First call after invalidation OR page-size change OR cursor empty → fallback path
    - `delta % pageSize == 0` AND `1 ≤ stepMagnitude ≤ 5` → `After`/`Before` with `pageStep = stepMagnitude - 1`
    - Otherwise → fallback path
  - Fallback: calls `_repository.GetManyAsync(predicate, new Options<TEntity> { Skip = skip, Limit = pageSize, Sort = BuildSortDefinition(sortBy, ascending) })`. After the fetch, re-issues cursors via `CursorToken.From(items[0], sortBy, ascending)` and `CursorToken.From(items[^1], sortBy, ascending)` so the next non-jump call can resume keyset navigation.
  - On non-fallback path: calls `_repository.GetPageAsync(pageSize, position, predicate, sortBy, ascending, ct)` and stores `page.FirstCursor` / `page.LastCursor`
  - Updates `_previousSkip = skip`, `_previousPageSize = pageSize`
  - Returns `(items, _totalCount)`
- [x] `Reset()` clears state (`_firstCursor`, `_lastCursor`, `_previousSkip`, `_previousPageSize`, `_queryCacheKey`, `_totalCount`)
- [x] **Built only on the public Layer 1 API.** No internal access. Any consumer can implement an equivalent class with their own state-management strategy.
- [x] Build clean

### Step 8: Tests
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
- [ ] New `Paging/CursorPagerTests.cs` — DB-backed:
  - Sequential next: `LoadAsync(0, 10, ...)` then `LoadAsync(10, 10, ...)` then `LoadAsync(20, 10, ...)` returns the right pages
  - Last page: `LoadAsync(40, 10, ...)` against a 50-doc collection → final 10 in correct order
  - Previous: `LoadAsync(20 ... )` then `LoadAsync(10, ...)` returns page 2 again
  - 2-page jump: `LoadAsync(0, 10, ...)` then `LoadAsync(20, 10, ...)` → uses `After` with `pageStep: 1`
  - Arbitrary jump: `LoadAsync(0, 10, ...)` then `LoadAsync(150, 10, ...)` → falls back to `GetManyAsync(skip)`, returns page 16; subsequent `LoadAsync(160, ...)` → uses keyset (cursor was re-issued from the fallback's last item)
  - Total-count cache: change the filter → `CountAsync` runs once for the new filter, then no count queries on subsequent same-filter loads
  - `Reset()` clears state — next `LoadAsync` re-runs CountAsync + treats Skip=0 as First

### Step 9: Index-plan verification test
- [ ] Add an explain-plan check (use `Disk.ExecuteAsync(...)` to run `explain` on a representative page query against a seeded collection that has the recommended `{sortField, _id}` index) — assert the winning plan stage is `IXSCAN`, not `COLLSCAN`
- [ ] Note: this is a guidance test, not an enforcement; running without the index still works, just slower

### Step 10: Build verification on all targets
- [ ] Build on net8 / net9 / net10 — clean, warnings under 50 budget

### Step 11: README update
- [ ] Extend the "Custom queries" section with a new "Keyset pagination" subsection covering:
  - Why (skip penalty on deep pages, "jump to last")
  - Basic usage — `GetPageAsync(pageSize, PagePosition.First, ...)`, then chaining via `LastCursor`/`FirstCursor`
  - Sort + filter combinator example
  - Index guidance: compound `{sortField, _id}` per sortable column
  - Total count: separate cached `CountAsync(predicate)` call
  - **Easy path**: using `CursorPager<TEntity, TKey>` in a Blazor grid (Radzen example)
  - **Manual path**: when you need full control, the ~60-line state-tracking pattern against the raw `GetPageAsync` + `CursorToken.From` API

### Step 12: Milestone commit + closure
- [ ] (Per-step commits along the way — this is the closure)
- [ ] Archive `plan/feature.md` → `$DOC_ROOT/Tharga/plans/Toolkit/MongoDB/done/keyset-pagination.md`
- [ ] Delete `$DOC_ROOT/Tharga/plans/Toolkit/MongoDB/planned/33-keyset-pagination.md`
- [ ] Update `$DOC_ROOT/Tharga/plans/Toolkit/MongoDB/planned/README.md`: drop #33 row; add to "Done" recent list. After this lands the planned/ directory is empty (only `README.md` remains) — note that explicitly so future planners know to look at cross-project `Requests.md` for the next slot
- [ ] Delete `plan/` directory from the repo
- [ ] Final commit: `feat: keyset-pagination complete`

### Step 13: Push + PR
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
