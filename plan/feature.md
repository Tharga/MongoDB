# Feature: `CollectionView` scales to many collections

Closes [#113](https://github.com/Tharga/MongoDB/issues/113) on merge.

## Goal

The Blazor admin's **Collections** tab freezes at scale (1767 collections, multi-agent call firehose): ~15s initial load + an unresponsive Blazor Server circuit after that, with no exception logged anywhere. Restructure the page so all three failure modes are removed and the page stays responsive regardless of collection count.

## Background

`Tharga.MongoDB.Blazor/CollectionView.razor` has three layered scale issues, in increasing order of architectural reach:

1. **Eager full load** ([CollectionView.razor:222–228](Tharga.MongoDB.Blazor/CollectionView.razor#L222-L228)). `OnParametersSetAsync` does `_all = await DatabaseMonitor.GetInstancesAsync(...).ToArrayAsync()` — synchronously materialises all 1767 collections plus their per-collection metadata (live Mongo `listCollections` + stats per context). Nothing renders until that's done.

2. **`OnCallChanged` is O(rows × callCounts), throttled to ≤ 2/sec** ([CollectionView.razor:149–167](Tharga.MongoDB.Blazor/CollectionView.razor#L149-L167)). `CallLibrary.NotifyChanged` is throttled to 500ms ([CallLibrary.cs:266](Tharga.MongoDB/CallLibrary.cs#L266)) and the handler runs `UpdateCallCounts`, which loops over every row in `_model` (1767) and for each row does `callCounts.Where(kvp => kvp.Key.EndsWith(suffix)).Sum(...)` over the whole call-counts dictionary, then `StateHasChanged()`. Under load that's hundreds of thousands of comparisons + a full-grid re-render twice a second.

3. **Whole-component re-render**. Every `StateHasChanged()` on `CollectionView` re-renders the full `RadzenDataGrid` for all rows even when one cell changed. Combined with #2 this saturates the Blazor Server circuit.

The issue's proposed design (verbatim, all 5 points) is what we're shipping.

## Scope

### 1. Algorithmic fix: `UpdateCallCounts` → O(M + N)

Build a suffix → sum index once per `OnCallChanged` tick, then look up each row's suffix in O(1). For 1767 rows and ~200 fingerprint keys that takes the ~350k-op pass down to ~2k.

The shape inside `CallLibrary.GetCallCounts()` doesn't change — but we add a `GetCallCountsBySuffix()` helper that materialises a `Dictionary<string, int>` keyed on `.{DatabaseName}.{CollectionName}`, so `CollectionView` can drop the LINQ scan.

### 2. Per-cell child components for Calls + Documents/Size

`CollectionModel` becomes a small change-notifier:

```csharp
public class CollectionModel
{
    public event Action Changed;
    public void NotifyChanged() => Changed?.Invoke();

    // existing fields...
}
```

Two new Razor components:

- `CallCountCell.razor` — takes `[Parameter] CollectionModel Model`, subscribes to `Model.Changed` in `OnInitialized`, owns its own `StateHasChanged`, unsubscribes in `Dispose`.
- `StatsCell.razor` — same shape, owns Documents + Size (one component renders both cells, or two thin ones — decide at impl).

`CollectionView.OnCallChanged` becomes: build the suffix→sum index once, update each `model.CallCount`, call `model.NotifyChanged()` per row whose value changed. No grid-wide `StateHasChanged`.

`CollectionView.OnCollectionInfoChanged` similarly: mutate the matching model's fields, then `model.NotifyChanged()`. The grid only triggers a full re-render when a row is **added or removed** (the existing `ReloadDataAsync` path stays).

Indices and Clean cells stay as today (per the user's per-cell scope decision — they change too infrequently to be worth restructuring).

### 3. Hand-rolled SWR cache for the initial load

New `CollectionInfoCache` singleton service in `Tharga.MongoDB.Blazor`:

```csharp
internal class CollectionInfoCache
{
    private readonly ConcurrentDictionary<string, (CollectionInfo Info, DateTime RefreshedAt)> _entries = new();
    public IReadOnlyCollection<CollectionInfo> GetAll();
    public void Upsert(CollectionInfo info);
    public bool IsStale(string key, TimeSpan maxAge);
}
```

`CollectionView.OnParametersSetAsync` renders from the cache **synchronously** if non-empty, then kicks off a background refresh task (queue-throttled per #4) that streams updated info into the cache and raises `Changed` on the matching models. First time the page is hit per process is still slow (one user pays); every subsequent navigation is instant.

No `Tharga.Cache` dependency — hand-rolled per the user's call. Single thin service, ~50 LOC.

### 4. Queue-throttled background revalidation with in-view priority

A simple priority-aware revalidator:

- `SemaphoreSlim(maxConcurrent: 16)` cap on concurrent per-collection Mongo refreshes (16 is a starting point — configurable later if it bites).
- Two queues: `_highPriorityKeys` (in-view rows) and `_lowPriorityKeys` (everything else).
- `RadzenDataGrid.PageChange` (or render event — pick at impl) feeds the visible page's collection keys into `_highPriorityKeys`.
- Background loop drains high priority first, falls back to low.
- After each refresh, the new `CollectionInfo` is upserted into the cache and the matching model is updated + `NotifyChanged()` is called.

### 5. Per-cell loading affordance

`CollectionModel.IsRevalidating` bool. The new `CallCountCell`/`StatsCell` components render a faint inline spinner or dimmed style when `IsRevalidating == true`. `CollectionView` toggles the flag around each per-row revalidation in #4. Sort order works against the **cached** value, so the affordance is purely visual.

## Out of scope

- **Tharga.Cache dependency.** Hand-rolled SWR (~50 LOC) per the design call.
- **Per-cell Indices / Clean components.** Low-frequency changes; not worth the restructure.
- **CallView scaling.** Mentioned in the issue as a related pattern under the same firehose, but a separate investigation. Not in this PR.
- **CollectionView responsiveness across multiple connected admin users.** The cache is per-process; multiple admin clients on the same server share it, but each Blazor circuit still pays its own UI rendering cost.
- **Persistent (cross-process) cache.** First load per process is still slow — the SWR cache is process-lifetime only.

## Acceptance criteria

- `UpdateCallCounts` is provably O(M + N) and verified by a unit test that pins the call-count lookup contract.
- The `Calls` and `Documents`/`Size` cells re-render in isolation when their model's `Changed` event fires (verified by a component test).
- After the first per-process load, navigating away and back to **Collections** renders the grid in under 200ms (cached); a background revalidation completes in the queue order.
- When the visible page changes, the visible rows are revalidated before off-screen rows (priority).
- Loading-affordance UI element appears on a cell while its model is being revalidated; clears when the refresh completes.
- The 5 pre-existing Lockable transaction-test failures remain the only failures; everything else stays green.

## Done condition

- Acceptance criteria met.
- Plan archived to `done/collection-view-scale.md`; `planned/README.md` updated.
- PR opens with `closes #113`.

## Effort

Large. Spans the Blazor project (Razor restructuring + 2 new components + new singleton service + background work) plus a small touch on `CallLibrary` for the suffix-indexed call counts. Estimate ~1–2 weeks; one cohesive PR per the user's scope call.

## NuGet

Current. No bumps needed (no new package dependencies — hand-rolled SWR).
