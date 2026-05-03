# Feature: Fix BySchema index assurance bug + Lockable index verification

## Originating Branch
develop

## Source
Eplicta outbox request (2026-05-02): `LockableRepositoryCollectionBase` should auto-register indexes on `Lock` fields. The user followed up: *"some developers suspect that the mode 'Schema' does not work as intended."*

## Investigation findings

### The lockable indexes ARE declared correctly

`LockableRepositoryCollectionBase<TEntity, TKey>.CoreIndices` already declares both indexes the lock-check query needs:

```csharp
internal override IEnumerable<CreateIndexModel<TEntity>> CoreIndices =>
[
    new(Builders<TEntity>.IndexKeys.Ascending(x => x.Lock),
        new CreateIndexOptions { Name = "Lock" }),
    new(Builders<TEntity>.IndexKeys
            .Ascending(x => x.Lock.ExceptionInfo)
            .Ascending(x => x.Lock.ExpireTime)
            .Ascending(x => x.Lock.LockTime),
        new CreateIndexOptions { Name = "LockStatus" })
];
```

These match the runtime lock-check filter (`Lock == null || (Lock.ExceptionInfo == null && Lock.ExpireTime < now)`).

### The real bug: `AssureIndexMode.BySchema` mispairs indexes

Three correctness issues in `UpdateIndicesBySchemaAsync` (`DiskRepositoryCollectionBase.cs:1666`) and `IndexMetaConverter.BuildIndexMetas`:

1. **Order mismatch + `Zip` pairing.** `UpdateIndicesBySchemaAsync` builds `indices = CoreIndices.Union(Indices)` but `BuildIndexMetas()` builds `definedIndiceModel = Indices.Union(CoreIndices)` — opposite orders. It then calls `indices.Zip(definedIndiceModel, …)` which pairs `CreateIndexModel` records with **the wrong** `IndexMeta` records. When the code decides "this schema is missing → create it", it runs `CreateOneAsync` against a model paired by position with an unrelated meta. Effect: indexes can be skipped or created incorrectly when both `CoreIndices` and `Indices` are non-empty.

2. **`BuildIndexMetas` ordering is the opposite of every other call site.** Every other place in the codebase concatenates `CoreIndices` first, then `Indices`. `BuildIndexMetas` does the reverse. Even if call sites stop using `Zip`, the monitor's "Defined" index list is in a different order than the "create" list — confusing for diagnostics.

3. **Eplicta's reported symptom is consistent with bug #1.** Lockable consumers like `HarvesterDocumentRepositoryCollection` typically declare their own `Indices` (e.g. business-specific compound keys). With `BySchema` mode set, the `Zip` mispairs lockable's `Lock` / `LockStatus` indexes against those consumer indexes. Result: under some shapes, the schema comparison returns false-positive "exists" for the lockable index → **never created in MongoDB** even though declared in code.

### Why `ByName` mode works

`UpdateIndicesByNameAsync` doesn't use `Zip` — it iterates `indices` directly and looks up by name. Order doesn't matter. That's why consumers using the default `ByName` mode aren't affected, and why this bug only surfaces for consumers (like Eplicta) on `BySchema`.

## Goal

Fix the `BySchema` mode so it correctly identifies missing indexes regardless of the order or count of `CoreIndices` / `Indices`. Add tests that lock the behaviour in. Address the original Eplicta concern via verification + a bulk re-apply helper for already-deployed environments.

## Scope

### Bug fixes

1. **Fix `UpdateIndicesBySchemaAsync`** — replace the `Zip` pairing with a lookup that finds the `CreateIndexModel` whose schema matches the `IndexMeta` it's compared against. Drop the dependency on positional ordering.

2. **Fix `BuildIndexMetas` ordering** — concatenate `CoreIndices` first, `Indices` second, to match every other site.

3. **Add duplicate-name validation to `BySchema` and `DropCreate`** — match the up-front check `ByName` already has. Throw `InvalidOperationException` when two defined indexes share a name (regardless of schema). Without this, a name collision between `CoreIndices` and consumer `Indices` causes the second `CreateOneAsync` to fail mid-loop, leaving the collection partially indexed.

4. **Add duplicate-schema warning to `BySchema`** — when two defined indexes have identical fields + uniqueness, log a warning at startup. Likely a copy-paste error — only one index ends up in MongoDB.

### Tests

3. **Regression tests for `BySchema` mode** in `Tharga.MongoDB.Tests`:
   - When a collection has both `CoreIndices` (lockable) and `Indices` (consumer), `BySchema` creates all missing indexes.
   - Order between `CoreIndices` and `Indices` doesn't change the outcome.
   - Behaviour matches `ByName` mode for the same input.
   - Schema-equality drops the right indexes (an existing one whose schema isn't in the defined list).

4. **Verification test** that fails if `LockableRepositoryCollectionBase.CoreIndices` ever drifts from the lock-check query shape.

### Bulk re-apply helper (still useful even with the fix)

5. **`IDatabaseMonitor.RestoreAllIndicesAsync`** — iterates `GetInstancesAsync()` and calls `RestoreIndexAsync(info, force: false)` per collection. With progress reporting + summary. Reason: even with the fix, existing tenant collections in prod (Eplicta has thousands) need a one-shot trigger to apply the now-correct indexes.

6. **Surface in `MonitorToolbar`** as "Assure all indices" with a notification per progress event.

7. **MCP tool `mongodb.restore_all_indexes`** in `Tharga.MongoDB.Mcp` so the operation can be triggered from an AI agent / one-shot script.

### Docs

8. **README** — short section explaining each mode's reconciliation semantics:
   - `ByName` (default, fast): names must be set; schema changes that keep the name are NOT detected — must rename to apply
   - `BySchema`: names optional; schema changes ARE detected; schema-equivalent renames in code are NOT applied (the existing index keeps its old MongoDB name)
   - `DropCreate`: names optional; nukes and rebuilds every assurance pass — slow but always converges
   - `Disabled`: skipped (still useful for read-only scenarios or one-shot deploys)
   Plus how to roll out a new index across already-deployed environments via the new bulk helper.

## Out of scope

- Auto-running the bulk helper on app startup. Keep it explicit so consumers control timing.
- Changing the lockable indexes — they're already correct.

## Acceptance Criteria

- [ ] `BySchema` mode creates missing indexes regardless of `CoreIndices` / `Indices` order or count
- [ ] `BuildIndexMetas` returns indexes in `CoreIndices`-first order (matches all other call sites)
- [ ] `BySchema` and `DropCreate` throw up front when two defined indexes share a name
- [ ] `BySchema` logs a warning when two defined indexes have identical schema
- [ ] Regression tests cover both single-source and combined-source index lists
- [ ] Verification test fails if lockable `CoreIndices` and the lock-check query drift apart
- [ ] `RestoreAllIndicesAsync` exists, no-op'd in `DatabaseNullMonitor`, mocked in test infra
- [ ] Blazor `MonitorToolbar` has an "Assure all indices" action
- [ ] `Tharga.MongoDB.Mcp` exposes `mongodb.restore_all_indexes`
- [ ] README documents the modes and the bulk helper
- [ ] Tests pass on net8/9/10 with the existing 50-warning budget

## Done Condition

Eplicta can roll out a release that uses `AssureIndexMode.BySchema` and have `LockableRepositoryCollectionBase`'s indexes correctly applied — both for newly-created collections (via the bug fix) and for already-existing tenant collections (via the bulk helper). Future drift between the lock-check query and the lock indexes is caught at build time by the verification test.
