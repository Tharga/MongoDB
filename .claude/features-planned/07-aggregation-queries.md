# Feature: Support aggregation queries without loading data into memory

## Source
Backlog (Medium priority)

## Goal
Enable aggregation queries (e.g. sum, count, avg) to be executed server-side via MongoDB aggregation pipeline, without loading documents into application memory.

## Scope
- Add aggregation query support to the repository base classes
- Support common aggregation operations (sum, count, avg, min, max)
- Example use case: document size calculation in FortDocs
- Ensure ExecuteAsync read-only constraints are respected

## Acceptance Criteria
- [ ] Consumers can run aggregation queries without loading full documents
- [ ] Common operations (sum, count, avg) are supported
- [ ] Results are computed server-side by MongoDB
- [ ] Tests cover aggregation scenarios

## Done Condition
Aggregation queries run server-side and return results without loading documents into memory.
