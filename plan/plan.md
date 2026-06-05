# Plan: `CollectionView` scales to many collections

Feature scope: see [feature.md](feature.md). Branch: `feature/collection-view-scale`. Closes #113.

## Steps

### Phase 1 — Algorithmic fix for `UpdateCallCounts`

- [ ] **1.1** Add `IReadOnlyDictionary<string, int> GetCallCountsBySuffix()` to `ICallLibrary` and implement on `CallLibrary`. Key shape: `.{DatabaseName}.{CollectionName}` (matches the existing `.EndsWith(suffix)` semantics so multiple ConfigurationNames with the same DB+collection sum correctly).
- [ ] **1.2** Refactor `CollectionView.UpdateCallCounts` to use the new helper — single dictionary lookup per model.
- [ ] **1.3** Refactor the matching `ReloadDataAsync` callsite (lines 256–301) to use the same helper.
- [ ] **1.4** Test: `CallLibraryTests.GetCallCountsBySuffix_SumsAcrossConfigurations` + a focused micro-bench note (commit message, not a perf test) showing the before/after for a synthetic 1767-collection / 200-key scenario.

### Phase 2 — Per-cell child components for Calls + Documents/Size

- [ ] **2.1** Add `event Action Changed` + `void NotifyChanged()` to `CollectionModel`. (Keep field initialization; no INotifyPropertyChanged — single coarse event is enough for our needs.)
- [ ] **2.2** New `Tharga.MongoDB.Blazor/Cells/CallCountCell.razor`:
  - `[Parameter] public CollectionModel Model { get; set; }`
  - Subscribes to `Model.Changed` in `OnInitialized`, calls `InvokeAsync(StateHasChanged)` in the handler, unsubscribes in `Dispose`.
  - Renders `@(Model.CallCount > 0 ? $"{Model.CallCount:N0}" : "")` plus the loading affordance from Phase 5.
- [ ] **2.3** New `Tharga.MongoDB.Blazor/Cells/StatsCell.razor`:
  - Same shape. Renders Documents OR Size depending on a `[Parameter] StatsField Field` enum, so one component file covers both cells.
- [ ] **2.4** Wire the new components into `CollectionView.razor`'s grid template (replace the inline `<Template>` blocks at lines 74–83 and 97–101).
- [ ] **2.5** Refactor `CollectionView.OnCallChanged`:
  - Build the suffix→sum dict once (Phase 1 helper).
  - For each model whose `CallCount` changed, assign + `model.NotifyChanged()`.
  - Drop the grid-wide `StateHasChanged()` call.
- [ ] **2.6** Refactor `CollectionView.OnCollectionInfoChanged` (lines 169–209):
  - Mutate the matching model's fields in place.
  - Call `model.NotifyChanged()` instead of `await InvokeAsync(StateHasChanged)`.
  - Keep the new-collection branch calling `await ReloadDataAsync()` (that one DOES need a grid re-render — composition changed).
- [ ] **2.7** Component-tested: `CallCountCellTests` confirms StateHasChanged fires when `Model.Changed` fires (bUnit or Razor.Test).

### Phase 3 — Hand-rolled SWR cache

- [ ] **3.1** New `Tharga.MongoDB.Blazor/Internal/CollectionInfoCache.cs`:
  ```csharp
  internal class CollectionInfoCache
  {
      private readonly ConcurrentDictionary<string, Entry> _entries = new();
      public record Entry(CollectionInfo Info, DateTime RefreshedAt);
      public IReadOnlyCollection<CollectionInfo> GetAll();
      public Entry Upsert(CollectionInfo info);
      public bool TryGet(string key, out Entry entry);
      public void Remove(string key);
  }
  ```
- [ ] **3.2** Register as singleton in `MongoDbBlazorRegistrationExtensions` (find the right registration entry point at impl — there's an `AddMongoDbAdmin` extension or similar).
- [ ] **3.3** Inject `CollectionInfoCache` into `CollectionView`.
- [ ] **3.4** `OnParametersSetAsync`:
  - If cache is non-empty: render from cache **synchronously** (no await; `_all = cache.GetAll().ToArray()`). Kick off background refresh task that streams from `DatabaseMonitor.GetInstancesAsync` and upserts each.
  - If cache is empty: do the existing eager load, populate cache as items stream in.
  - In both cases, the background refresh feeds Phase 4's revalidator.
- [ ] **3.5** Cache-eviction: `OnCollectionDropped` removes the key from the cache too.
- [ ] **3.6** Test: `CollectionInfoCacheTests` covers Upsert / TryGet / GetAll / Remove + concurrent access (no exceptions, last-write-wins).

### Phase 4 — Queue-throttled background revalidation with in-view priority

- [ ] **4.1** New `Tharga.MongoDB.Blazor/Internal/RevalidationQueue.cs`:
  - `SemaphoreSlim` cap on concurrent refreshes (start with 16; constant for now).
  - Two `ConcurrentQueue<string>` queues for high/low priority keys.
  - Loop: dequeue high before low; for each key, fetch from `IDatabaseMonitor.GetInstanceAsync(fingerprint)` and upsert into `CollectionInfoCache`, raise model's `Changed`.
  - Cancellable via `CancellationToken` so it can stop cleanly on dispose.
- [ ] **4.2** Wire `RevalidationQueue` into `CollectionView` as injected service (or own private background task per view — decide at impl based on whether multi-tab sharing matters).
- [ ] **4.3** Hook the Radzen grid's page-change event (`Page="@OnGridPaged"` or equivalent): collect the visible page's `CollectionModel.Key` values, enqueue to `_highPriorityKeys`.
- [ ] **4.4** All other rows go to `_lowPriorityKeys` on initial render of a cached `_all`.
- [ ] **4.5** Test: `RevalidationQueueTests` covers priority ordering, semaphore limit, cancellation.

### Phase 5 — Per-cell loading affordance

- [ ] **5.1** Add `bool IsRevalidating` to `CollectionModel`.
- [ ] **5.2** `CallCountCell` + `StatsCell` render a subtle inline indicator when `IsRevalidating == true` — italic + dimmed color, or a 12px Radzen spinner. Pick at impl based on what looks least intrusive.
- [ ] **5.3** `RevalidationQueue` sets `model.IsRevalidating = true` + `NotifyChanged()` before the fetch; sets back to `false` + `NotifyChanged()` after the upsert.
- [ ] **5.4** Verify the affordance doesn't shift cell width (avoid layout thrash).

### Phase 6 — Close-out

- [ ] **6.1** Single cohesive commit.
- [ ] **6.2** Push.
- [ ] **6.3** Archive plan to `done/collection-view-scale.md`, update `planned/README.md` Done section, `git rm -r plan`, final commit `feat: collection-view-scale complete`, push, open PR closing #113.

## Last session

Plan finalised after issue review + design discussion. User chose: **full design** (all 5 issue points), **hand-rolled SWR** (no Tharga.Cache dependency), **per-cell components for Calls + Documents/Size only** (Indices and Clean stay inline — they change too infrequently to be worth restructuring). Six phases; the first two are the immediate-impact ones (call-count loop + per-cell rendering); 3–5 layer on the SWR + queueing + affordance to fix the 15s initial load too. Awaiting go-ahead to start Phase 1.

## Open questions worth flagging at impl

- **`CollectionModel` is a class today (line 273 in CollectionView.razor)** — verify it's not a record (records' event support is fiddly). Promote to a top-level class if currently nested.
- **`CollectionModel.Key`** doesn't exist as a property yet — needs to be added or derived (`$"{ConfigurationName}.{DatabaseName}.{CollectionName}"`) for the priority queue's enqueue step.
- **Radzen DataGrid's page-change event** — confirm at impl which event fires when a user changes the visible page so we can identify the in-view rows. May be `Page` callback or `LoadData`.
- **`OnCollectionInfoChanged`'s new-collection branch** still needs `ReloadDataAsync()` (re-render). Confirm we're not breaking the filter logic there.
- **Background task lifetime** in the SWR cache + revalidator: decide whether they live for the lifetime of the `CollectionView` component (per-circuit) or for the lifetime of the singleton service (process-wide). Process-wide is what the issue calls for; verify circuit-disposal doesn't cancel it.
- **Sort by Calls / Documents / Size** — confirm Radzen sorts on the model's CURRENT field value at render time. With per-cell updates the row's position in the sort order may shift but the parent grid won't necessarily re-sort. May need to call `grid.Reload()` or `StateHasChanged` on the parent at a low cadence (every few seconds) to keep sort fresh.
