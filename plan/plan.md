# Plan: index-loss (#167, #168, #169)

- [x] NuGet update check — `dotnet outdated`: no outdated dependencies, no deps commit.
- [x] #167: test that a lockable `CreateOnGet` collection survives deleting its last document (fails first), then forward `CreateCollectionStrategy` in `GenericDiskRepositoryCollection`. — One-line forward; `Lockable_CreateOnGet_SurvivesDeletingItsLastDocument` failed before, passes after.
- [x] #168a: test that after `DropCollectionAsync` the next add recreates declared indexes; add `IInitiationLibrary.ResetIndexAssured` and call it from `DropCollectionAsync`. — Resets `IndexAssured` and clears failures instead of removing the entry (`ShouldInitiateIndex` requires the entry; the pool keeps the handle).
- [x] #168b: tests for `DropEmptyAsync` — kept when a unique index is declared, dropped otherwise; implement the guard. — `DeclaresUniqueIndex()` checks `CoreIndices` ∪ `Indices`; `CreateStrategy.DropEmpty` XML doc updated.
- [x] #169: tests for `DropIndex` (plain and lockable collection); share the undeclared-index computation with `UpdateIndicesBySchemaAsync`, drop only those, log each drop. — `SameSchema` / `UndeclaredIndexNames` shared; each drop logged at Warning.
- [x] Full test suite, commit per issue. — 810 total, 0 failed, 8 skipped (known skips).
- [~] Docs review (README, docs/articles, MCP README) — note DropEmpty unique-index behaviour change.
- [ ] Push branch for testing.

## README / docs changes needed at completion
- `CreateStrategy.DropEmpty`: does not drop a collection that declares a unique index.
- `mongodb.drop_index` description already says "drops indexes not declared in code" — now accurate; mention logging if useful.
