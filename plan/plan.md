# Plan — fix/drop-empty-race (#189)

## Steps

- [x] 0. Branch `fix/drop-empty-race` from `master`; NuGet check.
  - Only `MongoDB.Driver` 3.9.0 → 3.13.0 is outdated, and it is held on purpose (#158, comment in the csproj). Nothing applied, so no `chore(deps)` commit.
- [x] 0b. Test project: FluentAssertions 8.11.0 → AwesomeAssertions 9.6.0 (shared rule of 2026-10-25, "switches when it is next worked"; same change as Console #44). Own commit. Full suite: 878, 0 failed, 8 skipped.
- [x] 1. Configuration default: `MongoDbConfiguration.CreateCollectionStrategy` → `CreateStrategy?`, fallback `CreateOnAdd`, doc comment. Tests: `CreateCollectionStrategyConfigurationTests` (5). Three failed before the fix, confirming the default was `DropEmpty`, the general section was ignored and an unset loader value shadowed appsettings.
- [x] 2. Remove automatic drops (delete path and first use) and `protected virtual DropEmptyAsync`; mark `CreateStrategy.DropEmpty` `[Obsolete]`. Tests in `IndexLossTests`: `DeleteLastDocument_KeepsTheCollection`, `FirstUse_OfAnExistingEmptyCollection_KeepsIt` (both failed before the change). Replaced the two `DropEmpty_*` tests. No library code referenced `DropEmpty` after this, so no obsolete warnings.
- [x] 3. `DropIfEmptyAsync()` on `IRepositoryCollection`, `RepositoryCollectionBase`, disk and lockable. The drop itself moved to private `DropCollectionCoreAsync`, shared with `DropCollectionAsync`, so the inner drop is not intercepted a second time. Order: interceptors, unique guard, `GetCollectionAsync` (opens the firewall), existence, count with limit 1, drop. Tests: 6 in `IndexLossTests`, 2 in `InterceptorPipelineTests` (one interception when it drops; rejection keeps the collection).
- [x] 4. `MAJOR_MINOR` → `2.20` in `.github/workflows/build.yml`.
- [x] 5. Full test suite: 891, 0 failed, 8 skipped (baseline 878; +13 new).
- [x] 6. Docs (`docs:` commit): README strategy section rewritten around `DropIfEmptyAsync`; configuration examples use `CreateOnAdd`/`CreateOnGet`, the C# example is valid C# again, and `ResultLimit` (no longer a property) became `FetchSize` in those examples; session note removed from README and `docs/articles/transactions.md`; `docs/articles/collection-interceptors.md` caveat now says two operations drop a collection. Generated `docs/api` is gitignored and comes from the XML comments.
  - Not touched: README `## MongoDB Result Limit` still documents a `ResultLimit` setting that no longer exists. Unrelated to #189.

## Close-out (when the user says done)

- `Requests.md` → `## Follow-up`: Cache and Team should upgrade to 2.20 (empty collections are kept; no code change).
- Archive `plan/feature.md` to the Plan directory `done/drop-empty-race.md`, `git rm -r plan`, final commit.
- PR body: `Closes #189`, with release notes naming the breaking changes and `DropIfEmptyAsync`.

## README changes needed

Covered by step 6.

## Last session

2026-10-28: plan confirmed (keep the unique-index guard in `DropIfEmptyAsync`; make the configuration property nullable). Steps 0–6 done in three commits: assertions swap, fix, docs. Full suite 891 / 0 failed / 8 skipped. Pushed on the user's go-ahead and opened as **draft** PR #192 (draft so it cannot merge before the close-out commit removes `plan/`). Next: the user tests, says done, then close-out, then mark the PR ready.
