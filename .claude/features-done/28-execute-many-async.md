# Feature: `ExecuteManyAsync` — streaming projection/aggregation results

## Source
Requested from the Berakna project (consumer) in April 2026. The current `ExecuteAsync` API forces callers to return a materialised collection (e.g. `List<T>`) when they need to run a `Find().Project(...)` or `Aggregate(...)` query, because the cursor only exists inside the callback scope. This means the entire result set is loaded into memory before it can be iterated.

A `TODO` was left in [`JobRepository.cs`](https://github.com/icomdev/berakna) pointing at this missing capability.

Previously, consumers worked around this by calling the now-removed protected `GetCollection()` method, which bypassed the library's index-management features.

## Goal
Add a method that lets callers stream documents from a custom query (`Find().Project(...)`, `Aggregate(...)`, etc.) as `IAsyncEnumerable<T>` without materialising the entire result set, while still routing the execution through the library's index/operation management.

## Scope

### New method on the collection base class

```csharp
IAsyncEnumerable<T> ExecuteManyAsync<T>(
    Func<IMongoCollection<TEntity>, IAsyncEnumerable<T>> queryFactory,
    Operation op = Operation.Read,
    CancellationToken cancellationToken = default);
```

- The `queryFactory` receives the underlying `IMongoCollection<TEntity>` and returns an `IAsyncEnumerable<T>` (typically via `IFindFluent.ToAsyncEnumerable()` or `IAsyncCursor.ToAsyncEnumerable()`).
- The result is a lazy stream — no buffering.
- Execution flows through the same index-management / call-tracking path as `ExecuteAsync` (so it shows up in the admin UI, respects `Operation`, etc.).

### Covers both caller patterns
1. `Find(filter).Sort(...).Skip(...).Limit(...).Project<TProjection>(...).ToAsyncEnumerable()` — custom projections with paging
2. `Aggregate<TDoc>(pipeline).ToAsyncEnumerable()` — aggregation pipelines returning custom shapes

Both produce `IAsyncCursor<T>` and fit the same factory signature.

## Out of scope
- Replacing `ExecuteAsync` — the materialising overload stays for single-value or whole-collection returns.
- A separate method for aggregation vs. find — one generic method is sufficient; the caller picks the query shape.

## Acceptance Criteria
- [ ] `ExecuteManyAsync<T>` implemented on the collection base class
- [ ] Streaming is lazy (documents are yielded one at a time, not buffered into a list first)
- [ ] Execution is tracked/monitored on the same path as `ExecuteAsync` (index management, admin UI visibility, `Operation` semantics)
- [ ] Cancellation token is propagated to the cursor
- [ ] Tests cover: find-with-projection, aggregate pipeline, cancellation mid-iteration, exception in factory
- [ ] README updated with an example of each pattern

## Done Condition
Consumers like Berakna can replace their manual `Find().Project().ToAsyncEnumerable()` inside `ExecuteAsync(... return list)` with a single `ExecuteManyAsync(...)` call that streams results and removes the intermediate `List<T>` buffer.
