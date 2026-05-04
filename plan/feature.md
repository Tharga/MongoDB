# Feature: Keyset-paginated `GetPageAsync` / `GetPageProjectionAsync`

## Originating Branch
`feature/keyset-pagination` (off master, 2026-05-04)

## Source
[`$DOC_ROOT/Tharga/plans/Toolkit/MongoDB/planned/33-keyset-pagination.md`](../../../../Users/danie/SynologyDrive/Documents/Notes/Tharga/plans/Toolkit/MongoDB/planned/33-keyset-pagination.md), originating from an April 2026 design discussion around large-collection UI pagination (Radzen `RadzenDataGrid` and similar grids).

## Goal
Add a page-based read API that uses **keyset pagination** (aka seek-based) so navigation cost is O(log N) per page regardless of how deep into the collection the page sits — including "jump to last." `GetManyAsync` / `GetManyProjectionAsync` stay as the skip-based APIs for arbitrary random-access; the new methods are for the common first / next / previous / last UI flow.

## Scope

### New methods on `IRepositoryCollection<TEntity, TKey>` (and `RepositoryCollectionBase` abstract)

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

### Supporting types — namespace `Tharga.MongoDB.Paging`

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
    CursorToken FirstCursor,   // round-trip with Before(...) to step back
    CursorToken LastCursor,    // round-trip with After(...) to step forward
    bool HasNext,
    bool HasPrevious);

public readonly struct CursorToken
{
    public override string ToString();           // URL-safe string form
    public static CursorToken Parse(string s);   // throws FormatException on bad input
    public static CursorToken Empty { get; }     // sentinel for "no cursor"

    /// <summary>
    /// Construct a cursor that points at the given <paramref name="entity"/> under the given sort.
    /// Used by adapter code that falls back to skip-based queries (e.g. arbitrary page-number jumps in
    /// a Radzen grid) and then needs to resume keyset navigation from the boundary documents.
    /// </summary>
    public static CursorToken From<TEntity, TKey>(
        TEntity entity,
        Expression<Func<TEntity, object>> sortBy,
        bool ascending)
        where TEntity : EntityBase<TKey>;
}
```

`CursorToken` is opaque to callers. Internally it carries: the sort-field path, sort direction, the sort-field value at the boundary, and the document `_id` (for tiebreaker correctness on non-unique sort fields).

### Encoding choice for `CursorToken`

Base64URL of a small BSON document `{ f: <sortFieldPath>, d: 1|-1, v: <sortValue>, i: <_id> }`. BSON handles `ObjectId`, `DateTime`, `Guid`, decimal, null, etc. without custom converters and survives a round-trip exactly. Base64URL keeps the string form safe in URL paths and query strings.

### Navigation mapping

| Position | Filter | Sort | After fetch |
|---|---|---|---|
| `First` | predicate only | `{sort:asc}` (or per `ascending`) | as-is |
| `Last` | predicate only | flip the sort direction | reverse items in memory |
| `After(cursor, pageStep)` | predicate ∧ keyset(`>`) | as `ascending` | `Skip(pageStep × pageSize).Limit(pageSize)` |
| `Before(cursor, pageStep)` | predicate ∧ keyset(`<`) | flip direction | `Skip(pageStep × pageSize).Limit(pageSize)`, then reverse items in memory |

`pageStep == 0` is the common case (next / previous one page); `pageStep == 1` covers the "±2 pages" buttons. Bounded skip of one page has negligible cost.

### Tiebreaker for non-unique sort fields

```js
$or: [
  { sortField: { $gt: lastSortVal } },
  { sortField: { $eq: lastSortVal }, _id: { $gt: lastId } }
]
sort: { sortField: 1, _id: 1 }
```

For descending: flip `$gt → $lt`. When `sortBy == null`, sort is `_id` alone — no tiebreaker needed (_id is unique).

### Filter composition
`predicate` is ANDed with the cursor filter so existing semantics are preserved. Sort uses the driver's expression-to-field-path machinery (same path `IndexKeys.Ascending(expr)` uses) so renamed members via `BsonElement` are honored.

### Index guidance
Keyset pagination is fast only with a compound index `{sortField: 1, _id: 1}` (descending variant works because MongoDB scans indexes backwards). README includes a "Keyset pagination" subsection that calls out the index requirement, the count-elsewhere pattern, and Radzen integration.

### Lockable collections
`LockableRepositoryCollectionBase` overrides `GetPageAsync` / `GetPageProjectionAsync` with delegation to `Disk.GetPageAsync(...)` — same pattern as the other read methods.

### `CursorPager<TEntity, TKey>` — grid-agnostic state helper

```csharp
namespace Tharga.MongoDB.Paging;

public sealed class CursorPager<TEntity, TKey>
    where TEntity : EntityBase<TKey>
{
    public CursorPager(IRepositoryCollection<TEntity, TKey> repository);

    public async Task<(TEntity[] Items, long TotalCount)> LoadAsync(
        int skip,
        int pageSize,
        Expression<Func<TEntity, bool>> predicate = null,
        Expression<Func<TEntity, object>> sortBy = null,
        bool ascending = true,
        CancellationToken cancellationToken = default);

    public void Reset();   // forget cached cursors + total count (e.g. on filter reset)
}
```

Encapsulates the per-grid state: the previous skip, the previous page's first/last cursors, and a per-filter `TotalCount` cache. On each `LoadAsync` call it maps `(skip, pageSize)` to a `PagePosition`:

- `skip == 0` → `First`
- `skip + pageSize >= TotalCount` → `Last`
- `skip - previousSkip == pageSize` → `After(lastCursor)`
- `previousSkip - skip == pageSize` → `Before(firstCursor)`
- `±N pages within a small bound` → `After`/`Before` with `pageStep`
- arbitrary jump → falls back to skip-based `GetManyAsync`, then re-issues cursors via `CursorToken.From(...)` so subsequent prev/next stays on the keyset path

Re-runs `CountAsync(predicate)` only when the filter changes (cached by the predicate's text representation).

**The pager is built entirely on top of the public Layer 1 API** — same access any consumer has. Direct use of `GetPageAsync` + `PagePosition` + `CursorToken.From` remains supported and unchanged. Consumers that prefer manual state (URL-driven pagination, server-pushed updates, infinite scroll) skip the pager entirely.

#### Radzen integration shape (with the pager)

```csharp
// Component-level state — one CursorPager per grid
private readonly CursorPager<TeamEntity, ObjectId> _pager;

private async Task OnLoadData(LoadDataEventArgs args)
{
    var predicate = BuildPredicate(args.Filters);
    var (sortBy, ascending) = ParseSort(args.OrderBy);

    var (items, totalCount) = await _pager.LoadAsync(
        args.Skip ?? 0, args.Top ?? 10,
        predicate, sortBy, ascending);

    Items = items;
    Count = (int)totalCount;
}
```

Without the pager, the same logic in raw form is ~60 lines (state fields + the Skip→PagePosition decoder + the fallback re-issue) — also documented in the README for consumers who need full control.

### Total count
Not in `CursorPage<T>`. Callers call `CountAsync(predicate)` separately and cache per-filter. Doing a count on every page load would defeat the speed advantage; the spec is explicit about this.

## Out of scope (deferred)
- **Multi-column sort** (mixed direction). Single column is enough for the target UI; multi-column adds composite-cursor design complexity without a motivating consumer.
- **Total count inside `CursorPage<T>`** — separate, caller-controlled query (above). The `CursorPager` caches it per filter for ergonomics, but the raw API stays count-free.
- **Auto-keyset inside `GetManyAsync`** — too magical to be predictable.
- **Direct Radzen adapter component** — `CursorPager` is grid-agnostic; no Radzen package dependency.

## Acceptance criteria
- [ ] `GetPageAsync` and `GetPageProjectionAsync` on `IRepositoryCollection<TEntity, TKey>` and `RepositoryCollectionBase<TEntity, TKey>` (abstract)
- [ ] Implemented in `DiskRepositoryCollectionBase`; delegation override in `LockableRepositoryCollectionBase`
- [ ] `PagePosition`, `CursorPage<T>`, `CursorToken`, `CursorPager<TEntity, TKey>` under `Tharga.MongoDB.Paging`
- [ ] `CursorToken.ToString()` / `CursorToken.Parse(...)` round-trip safely; malformed input throws `FormatException` with a clear message
- [ ] `CursorToken.From<TEntity, TKey>(entity, sortBy, ascending)` constructs a token from an arbitrary entity (used by the pager's fallback path; available to direct consumers too)
- [ ] Cursor token carries sort-field path + direction so a token from one sort is rejected when used with a different sort (clear `InvalidOperationException` or similar)
- [ ] `CursorPager<TEntity, TKey>` encapsulates Skip→PagePosition decoding, per-filter total-count cache, fallback for arbitrary jumps; built only on the public Layer 1 API
- [ ] Compound-index requirement called out in README; explain-plan test confirms a representative seeded query uses an index scan, not a collection scan
- [ ] Filter composition: caller predicate ANDed with cursor filter; correctness verified with a dynamic filter change between pages
- [ ] Execution goes through the existing `_databaseExecutor` limiter and shows up in admin-UI call tracking as `Operation.Read`
- [ ] Tests cover:
  - [ ] First / Next / Previous / Last over a seeded collection
  - [ ] ±2 page navigation (`pageStep: 1`)
  - [ ] Sort on a unique field (e.g. `_id`)
  - [ ] Sort on a non-unique field (tiebreaker correctness across duplicate values)
  - [ ] Ascending and descending sort
  - [ ] Filter + cursor composition
  - [ ] Malformed cursor string → `FormatException`
  - [ ] Cursor from sort A used with sort B → clear rejection
  - [ ] Empty collection returns empty `CursorPage` with `HasNext` / `HasPrevious = false`
  - [ ] Projection variant returns the projected shape
  - [ ] Lockable collection delegates correctly
  - [ ] `CursorToken.From(entity, sortBy, ascending)` round-trips: token built from an entity navigates back to the same logical position
  - [ ] `CursorPager.LoadAsync` happy path — sequential next / previous / first / last produces the same items as direct `GetPageAsync` calls
  - [ ] `CursorPager.LoadAsync` falls back to skip-based on arbitrary jump; subsequent next/prev calls work correctly (cursors re-issued from fallback page boundaries)
  - [ ] `CursorPager` total-count cache: `CountAsync` is called once per filter, not once per page
- [ ] README "Custom queries" section gains a "Keyset pagination" subsection: basic usage, Radzen integration pattern, index guidance, total-count-cached-separately pattern
- [ ] Build + tests green on net8 / net9 / net10 within the 50-warning budget
- [ ] **Regression: full Disk + Lockable test suites pass without edits.**

## Done condition
A consumer building filter + sort + first/last/next/prev UIs over a large MongoDB-backed collection can navigate in O(log N) per page with no skip penalty on deep pages or "jump to last." `RadzenDataGrid`'s `PagingSummaryFormat` ("{N:N0} rows, page {X} of {Y}") works because the total count comes from a separate cached `CountAsync` and the page number comes from Radzen's grid state. This closes `planned/33` — the last queued planned feature in `Toolkit/MongoDB`.
