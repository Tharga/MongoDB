# Feature: Aggregation Queries

## Originating Branch
develop

## Goal
Enable server-side aggregation queries (sum, count, avg, min, max) and estimated document count without loading documents into application memory.

## Scope
1. Add `EstimatedCountAsync()` — exposes MongoDB's `EstimatedDocumentCountAsync` (metadata-based, no filter, very fast)
2. Add `AggregateAsync<TResult>()` — generic aggregation pipeline execution
3. Add convenience methods for common aggregations: `SumAsync`, `AvgAsync`, `MinAsync`, `MaxAsync` on a specified field
4. All aggregation runs server-side via MongoDB aggregation pipeline
5. Respect `ExecuteAsync` read-only constraints and limiter

## Acceptance Criteria
- [x] `EstimatedCountAsync()` is available on repository collections
- [x] ~~`AggregateAsync`~~ — not needed, consumers use `ExecuteAsync` for arbitrary pipelines
- [x] Convenience methods (Sum, Avg, Min, Max) work on specified fields
- [x] All operations run server-side without loading documents
- [x] Tests cover all aggregation scenarios (8 tests)
- [x] README updated

## Done Condition
Consumers can run aggregation queries server-side and get results without loading documents into memory.
