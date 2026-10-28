# Fix #189 — stop dropping empty collections on the write path

## Goal

Remove the `DropEmpty` count-then-drop race ([#189](https://github.com/Tharga/MongoDB/issues/189)) by never
dropping a collection automatically. An empty collection is dropped only by an explicit maintenance call.
Ships as **2.20.0**, per the decision recorded on the issue (2026-10-27).

## Scope

1. **Default `CreateCollectionStrategy` becomes `CreateOnAdd`.**
   `MongoDbConfiguration.CreateCollectionStrategy` becomes `CreateStrategy?`, so the configuration cascade works
   the way it already does for `AutoClean`, `CleanOnStartup` and `FetchSize`. Today the property is non-nullable
   and the named section is always bound to a new object, so an unset value reads as `DropEmpty` (enum zero):
   - the `?? CreateStrategy.DropEmpty` fallback in `RepositoryConfiguration.GetConfiguration` is never reached,
     so changing that line alone would change nothing;
   - the general `MongoDB:CreateCollectionStrategy` setting is never consulted;
   - a `ConfigurationLoader` configuration that does not set the strategy overrides `appsettings`.
2. **No automatic drops.** Remove the `DropEmptyAsync` calls after a delete (`DiskRepositoryCollectionBase`,
   `ExecuteAsync`) and on first use (`FetchCollectionAsync`), and the `protected virtual
   DropEmptyAsync(IMongoCollection<TEntity>)` itself.
3. **`CreateStrategy.DropEmpty` stays, marked `[Obsolete]`, and behaves as `CreateOnAdd`.** Existing
   `"CreateCollectionStrategy": "DropEmpty"` settings keep binding; code overriding it gets a warning naming the
   replacement.
4. **Explicit maintenance call `Task<bool> DropIfEmptyAsync()`** on `IRepositoryCollection`, beside
   `DropCollectionAsync`, implemented on the disk and lockable bases.
   - Intercepted once, as `DropIfEmptyAsync` with `Operation.Delete`; the drop inside it is not intercepted again.
   - Returns `true` only when it dropped the collection; `false` when the collection does not exist, holds
     documents, or declares a unique index (the guard from #167–#169 stays).
   - Same bookkeeping as `DropCollectionAsync`: resets index assurance and raises `CollectionDropped`.
   - XML docs state the precondition: the count and the drop are two steps, so call it only while nothing writes
     to the collection (a retired per-part collection, a maintenance window).
5. **Version 2.20** (`MAJOR_MINOR` in `.github/workflows/build.yml`).
6. **Docs:** README (strategy section, configuration examples, transactions note),
   `docs/articles/transactions.md`, `docs/articles/collection-interceptors.md`.

## Out of scope

- An atomic drop-if-empty: MongoDB has none, and the alternatives were rejected on #189.
- A monitor, Blazor or MCP action that drops empty collections.
- `MongoDB.Driver` 3.9.0 → 3.13.0: held on purpose, #158.

## Breaking changes (why this is a minor)

- Behaviour: empty collections are no longer dropped, by default or with `DropEmpty`.
- `MongoDbConfiguration.CreateCollectionStrategy`: `CreateStrategy` → `CreateStrategy?`. Setting it still
  compiles; code reading it into a `CreateStrategy` does not, and binaries built against 2.19 must be rebuilt.
- `protected virtual DropEmptyAsync(IMongoCollection<TEntity>)` removed.
- New member `DropIfEmptyAsync()` on `IRepositoryCollection` and `RepositoryCollectionBase`.

## Consumers

`Tharga.Cache.MongoDB` (`CacheRepositoryCollection`) and `Tharga.Team.MongoDB` / `Tharga.Team.Service` (about a
dozen collections) all use the default strategy and delete documents, so today they are exposed to the race. The
cache is the clearest case: entries are evicted while others are written. After 2.20 their empty collections
stay. Neither needs a code change. Follow-up rows in `Requests.md` are added at close.

## Acceptance criteria

- Deleting the last document never drops the collection, whatever the strategy, including the obsolete `DropEmpty`.
- The first use of an existing empty collection does not drop it.
- Configuration: nothing set → `CreateOnAdd`; the general `MongoDB` section applies; a named section overrides it;
  a `ConfigurationLoader` value overrides `appsettings`; an unset loader value falls through to `appsettings`.
- `DropIfEmptyAsync`: drops an empty collection and the next add re-creates its indexes; keeps a non-empty one;
  keeps one that declares a unique index; returns `false` for a missing one; fires exactly one interception.
- The full test suite passes.

## Done when

All acceptance criteria are met, docs are updated, and the user confirms. The PR closes #189.
