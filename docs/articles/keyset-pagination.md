# Keyset pagination

`GetPageAsync` and `GetPageProjectionAsync` page through a collection by *cursor*, not by `skip`/`limit`. Cost is O(log N) per page regardless of how deep the page sits — there is no skip penalty when paging into the millions of documents and no spike when a user clicks "jump to last." Single-column sort with a `_id` tiebreaker; total count is intentionally not part of the result and should be fetched separately via `CountAsync(predicate)` (cache it client-side; it changes far less often than the page).

## Basic usage

```csharp
var first = await collection.GetPageAsync(
    pageSize: 25,
    position: PagePosition.First,
    sortBy: e => e.CreatedAt,
    ascending: false);

// "Next page" — feed the previous page's LastCursor back in
var next = await collection.GetPageAsync(
    pageSize: 25,
    position: PagePosition.After(first.LastCursor),
    sortBy: e => e.CreatedAt,
    ascending: false);

// "Previous page" — feed the current page's FirstCursor in via Before
var prev = await collection.GetPageAsync(
    pageSize: 25,
    position: PagePosition.Before(next.FirstCursor),
    sortBy: e => e.CreatedAt,
    ascending: false);
```

`PagePosition` has four factories:

| Position | Use |
|---|---|
| `PagePosition.First` | First page in sort order |
| `PagePosition.Last` | Final `pageSize` items in sort order (the trailing `pageSize` items, slid to align with the page boundary) |
| `PagePosition.After(cursor, pageStep = 0)` | Page after the cursor; `pageStep` skips that many extra pages forward |
| `PagePosition.Before(cursor, pageStep = 0)` | Page before the cursor; `pageStep` skips that many extra pages backward |

`CursorPage<T>` exposes `Items`, `FirstCursor`, `LastCursor`, `HasNext`, `HasPrevious`. The cursors are opaque, URL-safe strings (Base64URL of a small BSON doc) — store them in query strings, hidden form fields, or component state.

## Sort + filter

```csharp
var page = await collection.GetPageAsync(
    pageSize: 50,
    position: PagePosition.After(cursor),
    predicate: e => e.Status == "active",
    sortBy: e => e.Name,
    ascending: true);
```

Predicates compose with the keyset filter — the page is the items matching `predicate` strictly past `cursor` in the sort order.

## Index guidance

For each `(sortBy, ascending)` you page on, create the compound index `{ sortField: ±1, _id: ±1 }`:

```csharp
public override IEnumerable<CreateIndexModel<MyEntity>> Indices =>
[
    new(Builders<MyEntity>.IndexKeys.Ascending(e => e.CreatedAt).Ascending(e => e.Id),
        new CreateIndexOptions { Name = "createdAt_id" }),
];
```

Without the compound index the query still works but degrades to a sort + scan. With it, the planner uses an `IXSCAN` that walks straight to the cursor boundary and reads only the page-size's worth of documents.

## Total count

```csharp
var total = await collection.CountAsync(e => e.Status == "active");
```

`CountAsync` is a separate query because counts are typically far cheaper to cache than pages — once per filter-change, not once per page-flip. Don't bake it into the paging hot path.

## `CursorPager<TEntity, TKey>` — easy path for grids

Most grid components (Radzen `RadzenDataGrid`, MudBlazor `MudDataGrid`, etc.) emit a `(skip, pageSize)` request on user navigation. `CursorPager` adapts that shape to the keyset API: it tracks the previous page's cursors, decodes the skip-delta into the appropriate `PagePosition`, falls back to skip-based `GetManyAsync` when the user does an arbitrary jump (e.g. clicking "page 17 of 200"), and re-issues cursors from the fallback's boundary documents so the next prev/next call resumes the keyset path.

```csharp
private CursorPager<Order, ObjectId> _pager;

protected override void OnInitialized()
{
    _pager = new CursorPager<Order, ObjectId>(_orders);
}

private async Task LoadDataAsync(LoadDataArgs args)
{
    var (items, total) = await _pager.LoadAsync(
        skip: args.Skip ?? 0,
        pageSize: args.Top ?? 25,
        predicate: BuildPredicate(args.Filter),
        sortBy: o => o.CreatedAt,
        ascending: false);

    _orders = items;
    _totalCount = (int)total;
}

private void OnFilterChanged() => _pager.Reset();
```

`CursorPager` caches the total count per `(predicate, sortBy, ascending)` cache key — when any of those change it re-runs `CountAsync` and clears the cursors. `Reset()` clears all state (use it when the underlying data is known to have changed underfoot).

## Manual path

When you need full control — e.g. cursor links shared between users, or persistence across sessions — work with `GetPageAsync` directly and stash `FirstCursor`/`LastCursor` wherever fits your application. `CursorToken` round-trips through `ToString()` and `CursorToken.Parse(string)` so it's safe to put in URLs, cookies, or hidden form fields. `CursorToken.From(entity, sortBy, ascending)` lets you mint a cursor pointing at any specific document — useful when restoring grid state from a deep link.

```csharp
var anchorToken = CursorToken.From<Order, ObjectId>(anchor, o => o.CreatedAt, ascending: false);
var page = await collection.GetPageAsync(25, PagePosition.After(anchorToken),
    sortBy: o => o.CreatedAt, ascending: false);
```

Cursors are sort-bound: passing one issued for `sortBy: x => x.Name` to a call sorting by `x => x.CreatedAt` throws `InvalidOperationException`. This is intentional — silently re-sorting would return wrong results.

## See also

- [API: IRepositoryCollection&lt;TEntity, TKey&gt;](xref:Tharga.MongoDB.IRepositoryCollection`2)
- [API: CursorToken](xref:Tharga.MongoDB.Paging.CursorToken)
- [API: CursorPager&lt;TEntity, TKey&gt;](xref:Tharga.MongoDB.Paging.CursorPager`2)
