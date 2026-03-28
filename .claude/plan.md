# Plan: Aggregation Queries

## Steps

### Step 1: Add EstimatedCountAsync
- [x] Add `EstimatedCountAsync()` to `IReadOnlyRepositoryCollection<TEntity, TKey>`
- [x] Add abstract method to `RepositoryCollectionBase`
- [x] Implement in `DiskRepositoryCollectionBase` (expose existing internal usage)
- [x] Delegate in `LockableRepositoryCollectionBase`
- [x] Write tests
- [x] Build and run tests

### Step 2: Add convenience aggregation methods (Sum, Avg, Min, Max)
- [x] Add `SumAsync`, `AvgAsync`, `MinAsync`, `MaxAsync` to interface
- [x] Implement in `DiskRepositoryCollectionBase` using aggregation pipeline via ExecuteAsync
- [x] Support optional filter predicate
- [x] Delegate in `LockableRepositoryCollectionBase`
- [x] Write tests (8 tests total, all pass)
- [x] Build and run tests (216 passed, 8 skipped)

### Step 3: Update README and commit
- [x] Update README with aggregation documentation
- [x] Final build and test (216 passed, 8 skipped)
- [ ] Commit all changes

## Notes
- Generic `AggregateAsync` is NOT needed — consumers can already use `ExecuteAsync` which provides raw `IMongoCollection<TEntity>` access for arbitrary pipelines.
