# Feature: index-loss

## Goal
Stop indexes from silently disappearing. Fixes GitHub issues #167, #168 and #169, all reported from a production job-queue incident on 2026-10-02 where a unique index was lost and could no longer be rebuilt.

## Scope
1. **#167** — `GenericDiskRepositoryCollection` forwards `CreateCollectionStrategy` to its proxy, so a lockable collection declaring `CreateOnGet` is no longer dropped when a delete empties it.
2. **#168** — after a collection is dropped, the next write re-assures its indexes before writing:
   - on drop, reset the collection's `IndexAssured` flag and clear its failed-index records in `InitiationLibrary` (not remove the entry: `ShouldInitiateIndex` requires it, and the collection pool keeps serving the cached handle);
   - `DropEmptyAsync` never auto-drops a collection that declares a unique index, since other processes cannot learn about the drop.
3. **#169** — `DropIndex` (MCP `mongodb.drop_index`, Blazor "Drop index", monitor remote action) drops only indexes whose schema matches nothing in `CoreIndices` ∪ `Indices`, using the same schema comparison as `UpdateIndicesBySchemaAsync`. `_id_` and declared indexes (including lockable `Lock` / `LockStatus`) are kept. Each dropped index is logged at Information.

## Out of scope
- Periodic sweep for indexes that went missing in other processes (decided against; the unique-index DropEmpty guard covers the common case).
- Changing the MCP access level of `drop_index` (no longer destructive beyond its description).

## Acceptance criteria
- A lockable collection with `CreateOnGet` survives deleting its last document (test).
- After `DropCollectionAsync`, the next add to the same collection instance recreates the declared indexes before the document is written (test).
- A `DropEmpty` collection with a declared unique index is not dropped when emptied; one without is still dropped (tests).
- `DropIndex` keeps declared and `_id_` indexes, drops undeclared ones, returns correct before/after counts (tests, including a lockable collection).
- Full test suite passes.

## Done condition
All acceptance criteria met, docs reviewed, issues #167, #168, #169 commented and closed by the PR.
