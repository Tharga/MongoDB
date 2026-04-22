# Feature: Rebase `GetAsync` / `GetProjectionAsync` on driver cursor (no library-level skip)

## Source
Internal refactor motivated by the April 2026 pagination/streaming discussion that led to feature #28 (`ExecuteManyAsync`). While implementing #28 it became clear that the existing streaming `GetAsync` and `GetProjectionAsync` still use library-level skip-and-limit paging under the hood — so deep iteration on large collections pays MongoDB's skip penalty, which grows with how far in you've iterated.

## Goal
Replace the skip-based page loop in `GetAsync` / `GetProjectionAsync` with a MongoDB driver cursor (`find + getMore`). The public API stays identical; the implementation stops using `Skip`. Deep iteration becomes O(batches) instead of O(N²) in pagination terms, and the result is also more stable under concurrent writes.

## Scope

### What changes
- [`DiskRepositoryCollectionBase.GetAsync(FilterDefinition<TEntity>, Options<TEntity>, CancellationToken)`](../../Tharga.MongoDB/Disk/DiskRepositoryCollectionBase.cs#L247) — rewrite the internal `while (true) { GetManyAsync(Skip=...) }` loop into an async enumerator driven by `IMongoCollection<TEntity>.Find(...).ToCursorAsync()` + `MoveNextAsync`.
- [`DiskRepositoryCollectionBase.GetProjectionAsync(...)`](../../Tharga.MongoDB/Disk/DiskRepositoryCollectionBase.cs#L300) — same treatment, with the projection applied on the fluent find.
- Each DB round-trip (initial `find`, each `getMore`) goes through the same `_databaseExecutor` limiter pattern that `ExecuteManyAsync` uses (see feature #28). In-batch iteration does not hold a limiter slot.
- Honour the existing caller-visible behaviours: `Options.Skip`, `Options.Limit`, `Options.Sort`, `Options.Filter`, `ResultLimit`, `FetchSize`. If the caller specified `Skip`, it is applied to the find (single Skip of bounded size is fine — the pathological case is growing-skip across pages, which goes away).
- Update the `_logger.LogDebug("Query on collection {collection} returned {pages} pages ...")` wording to reflect "batches" instead of "pages," or drop it.

### What does not change
- Public signatures of `GetAsync` / `GetProjectionAsync` — no breaking change.
- `GetManyAsync` / `GetManyProjectionAsync` — keep using Skip as documented; caller asked for a specific page.
- Instrumentation / admin-UI tracking — `FireCallStartEvent` / `OnCallEnd` still fire exactly once per `GetAsync` call (covering the whole stream), not once per batch.

## Out of scope
- Changes to `GetManyAsync` / `GetManyProjectionAsync` — deliberate caller-driven pagination, skip is appropriate.
- Keyset pagination as a caller-visible API — that is feature #33.
- Changes to `ResultLimit` semantics.

## Stale `GetPageAsync` references to clean up as part of this work
- [`Tharga.MongoDB.Tests/GetPageAsyncTest.cs`](../../Tharga.MongoDB.Tests/GetPageAsyncTest.cs) — file contains only commented-out tests; delete.
- [`README.md`](../../README.md) line ~762 mentions `GetPageAsync`; remove or replace with a reference to the upcoming cursor-based `GetPageAsync` from feature #33.

## Acceptance Criteria
- [ ] `GetAsync` and `GetProjectionAsync` no longer call `GetManyAsync(Skip=...)` internally
- [ ] Implementation opens one driver cursor per call and iterates via `MoveNextAsync`
- [ ] Limiter slot is released during in-batch iteration (matches `ExecuteManyAsync` pattern)
- [ ] Honours `Options.Skip`, `Options.Limit`, `Options.Sort`, `Options.Filter`
- [ ] Existing `GetAsyncTest` / `GetProjectionAsync` tests still pass with no changes
- [ ] New test: iterate a collection with > `FetchSize` rows, confirm all rows returned exactly once, confirm no skip-penalty behaviour (e.g. latency per batch stays roughly constant from start to end)
- [ ] New test: iterating while another writer inserts/deletes doesn't produce duplicates or missing rows beyond what driver snapshot semantics allow (weaker guarantee than today's possibly-unstable skip paging — document it)
- [ ] Stale `GetPageAsync` test file and README line removed

## Done Condition
`GetAsync` and `GetProjectionAsync` stream the full result set with no library-level skip. Deep iteration cost is dominated by batch size × batch count, not by page number. Callers see no API change.
