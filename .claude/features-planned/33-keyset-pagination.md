# Feature: Keyset-paginated `GetPageAsync` / `GetPageProjectionAsync`

## Source
April 2026 design discussion around large-collection UI pagination. Target use case: Radzen `RadzenDataGrid` (and similar grids) with filter + single-column sort and navigation via first / previous / next / last / ±2 pages. `PagingSummaryFormat` typically reads "{N:N0} rows, page {X} of {Y}".

`GetAsync` / `GetProjectionAsync` will be rebased on a driver cursor in feature #32, which fixes streaming. This feature addresses the other half of the problem: **page-based** UI access over large collections without paying the MongoDB skip penalty on deep pages (especially "jump to last").

## Goal
Add a new page-based read method that uses keyset pagination (aka seek-based pagination) so navigation cost is O(log N) per page regardless of how deep in the collection the page sits. No change to `GetManyAsync` / `GetManyProjectionAsync`; those remain the skip-based APIs callers reach for when they really do want arbitrary random-access paging.

## Scope

### New method
Reuses the name `GetPageAsync` — the old method of that name is already gone from the implementation; only stale references remain (those are cleaned up in feature #32).

```csharp
Task<CursorPage<TEntity>> GetPageAsync(
    int pageSize,
    PagePosition position,
    Expression<Func<TEntity, bool>> predicate = null,
    Expression<Func<TEntity, object>> sortBy = null,   // null → sort by _id
    bool ascending = true,
    CancellationToken cancellationToken = default);

Task<CursorPage<T>> GetPageProjectionAsync<T>(
    int pageSize,
    PagePosition position,
    Expression<Func<TEntity, T>> projection,
    Expression<Func<TEntity, bool>> predicate = null,
    Expression<Func<TEntity, object>> sortBy = null,
    bool ascending = true,
    CancellationToken cancellationToken = default);
```

### Supporting types

```csharp
public readonly struct PagePosition
{
    public static PagePosition First { get; }
    public static PagePosition Last  { get; }
    public static PagePosition After(CursorToken cursor, int pageStep = 0);
    public static PagePosition Before(CursorToken cursor, int pageStep = 0);
}

public sealed record CursorPage<T>(
    T[] Items,
    CursorToken FirstCursor,   // round-trip with Before(...) to go back one page
    CursorToken LastCursor,    // round-trip with After(...) to go forward one page
    bool HasNext,
    bool HasPrevious);

public readonly struct CursorToken
{
    // Opaque to callers. Encodes (sortValue, _id) plus the sort field name and direction
    // so stale cursors from a different sort can be detected and rejected cleanly.
    public override string ToString();           // URL-safe string form
    public static CursorToken Parse(string s);   // throws FormatException on bad input
    public static CursorToken Empty { get; }     // sentinel for "no cursor"
}
```

### Navigation mapping
- **First page**: `GetPageAsync(pageSize, PagePosition.First, ...)` — no cursor filter, sort ascending (or per `ascending` arg), limit `pageSize`.
- **Last page**: `GetPageAsync(pageSize, PagePosition.Last, ...)` — no cursor filter, flip sort direction internally, limit `pageSize`, reverse items in memory before returning.
- **Next**: `GetPageAsync(pageSize, PagePosition.After(lastPage.LastCursor), ...)` — `{sortField: {$gt: sortVal}}` (plus `_id` tiebreaker), sort ascending, limit `pageSize`.
- **Previous**: `GetPageAsync(pageSize, PagePosition.Before(lastPage.FirstCursor), ...)` — `{sortField: {$lt: sortVal}}` (plus tiebreaker), sort descending, limit `pageSize`, reverse in memory.
- **±2 pages**: same as next/prev but with `pageStep: 1` — adds a bounded `Skip(pageSize)` to the cursor-seeked query. Small skip (one page) has no meaningful penalty.

### Tiebreaker for non-unique sort fields
For any `sortBy` that is not guaranteed unique, the filter uses a composite cursor on `(sortField, _id)`:
```
$or: [
  { sortField: {$gt: lastSortVal} },
  { sortField: {$eq: lastSortVal}, _id: {$gt: lastId} }
]
sort: { sortField: 1, _id: 1 }
```
For descending, flip `$gt → $lt`.

When `sortBy == null`, sort is `_id` alone (no tiebreaker needed; `_id` is unique).

### Filter composition
The caller's `predicate` is ANDed with the cursor filter so existing filtering semantics are preserved.

### Index guidance
Efficient operation requires a compound index matching the sort: `{sortField: 1, _id: 1}` (or descending variant — MongoDB can scan indexes backwards, so one index serves both directions). README will include guidance that callers should index each column they allow sorting on.

### Total count
Not included in `CursorPage<T>`. Callers call `CountAsync(predicate)` separately and cache per-filter. This keeps page navigation fast; a count query on every page load would be wasteful.

### Integration with `RadzenDataGrid`
Not part of the library itself, but documented in the README: Blazor component keeps `(LastCursor, FirstCursor)` state, handles Radzen's `LoadData` event, and maps Radzen Skip values to `PagePosition`:
- `Skip == 0` → `First`
- `Skip == Count - PageSize` → `Last`
- `Skip == prev + PageSize` → `After(lastCursor)`
- `Skip == prev - PageSize` → `Before(firstCursor)`
- other Skip values → fall back to a plain `GetManyAsync(Skip, Limit)` call (rare in this UI).

### Lockable collections
`LockableRepositoryCollectionBase` overrides with a plain delegation to `Disk.GetPageAsync(...)` / `Disk.GetPageProjectionAsync(...)`. Same pattern as the other read methods.

## Out of scope
- Multi-column sort (mixed direction). The target UI sorts on one column at a time; adding multi-column support adds meaningful design complexity (composite cursors over N fields, index direction constraints) without a motivating consumer.
- Total count inside `CursorPage<T>` — keep it a separate, caller-controlled query.
- Transparent "auto-keyset" inside `GetManyAsync` that detects sequential navigation patterns — more magic than it's worth.
- Direct `RadzenDataGrid` adapter component — document the integration pattern in the README; don't ship a Radzen dependency.

## Acceptance Criteria
- [ ] `GetPageAsync` and `GetPageProjectionAsync` on `IRepositoryCollection<TEntity, TKey>` and `RepositoryCollectionBase<TEntity, TKey>`
- [ ] Implemented in `DiskRepositoryCollectionBase`; delegation override in `LockableRepositoryCollectionBase`
- [ ] `PagePosition`, `CursorPage<T>`, `CursorToken` types in a sensible namespace (suggest `Tharga.MongoDB.Paging`)
- [ ] Cursor token string form round-trips safely; malformed tokens throw `FormatException` with a clear message
- [ ] Cursor token carries sort field name + direction so tokens from one sort are rejected when used with a different sort (throw with a clear message)
- [ ] Uses compound index `{sortField, _id}` for non-unique sort fields — verified via explain plan in a test
- [ ] Filter composition: caller predicate is ANDed with cursor filter; correctness confirmed under dynamic filter changes
- [ ] Execution goes through `_databaseExecutor` limiter and is visible in admin-UI call tracking as a `Read`
- [ ] Tests cover:
  - [ ] first, next, previous, last over a seeded collection
  - [ ] ±2 page navigation (`pageStep: 1`)
  - [ ] sort on a unique field (e.g. `_id`)
  - [ ] sort on a non-unique field (tiebreaker correctness across duplicate values)
  - [ ] ascending and descending sort
  - [ ] filter + cursor composition
  - [ ] malformed cursor string → `FormatException`
  - [ ] cursor from sort A used with sort B → clear rejection
  - [ ] empty collection returns empty `CursorPage` with correct `HasNext`/`HasPrevious = false`
  - [ ] projection variant returns projected shape
  - [ ] lockable collection delegates correctly
- [ ] README "Custom queries" section extended with a "Keyset pagination" subsection covering: basic usage, Radzen integration pattern, index guidance, total-count pattern

## Done Condition
Consumers building filter + sort + first/last/next/prev UIs over large MongoDB-backed collections can navigate in O(log N) per page, with no skip penalty on deep pages or "jump to last." The `PagingSummaryFormat` of `RadzenDataGrid` continues to work ("{N:N0} rows, page {X} of {Y}") because total count comes from a separate cached `CountAsync` call and page number comes from Radzen's grid state.
