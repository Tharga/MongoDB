# Feature: Tolerate duplicate `(configuration, collectionName)` in static lookup

References Florida's `Requests.md` entry (2026-05-30).

## Goal

Stop `DatabaseMonitor` from crashing the `/developer/database` page with `ArgumentException: An item with the same key has already been added` when two `IDiskRepositoryCollection<,>` classes legitimately target the same physical Mongo collection (the read-projection pattern).

## Background

[`DatabaseMonitor.cs:1325-1326`](../Tharga.MongoDB/DatabaseMonitor.cs) builds `_staticLookup` by calling `.ToDictionary(...)` directly:

```csharp
_staticLookup = GetStaticCollectionsFromCodeCore()
    .ToDictionary(x => (x.ConfigurationName ?? _options.DefaultConfigurationName, x.CollectionName), x => x);
```

`GetStaticCollectionsFromCodeCore` yields one `StatColInfo` per `IDiskRepositoryCollection<,>` class registered in `_mongoDbInstance.RegisteredCollections`. Two registered classes are legitimately allowed to overlay the same physical Mongo collection as read projections — e.g. Florida registers both `TeamRepositoryCollection<TeamEntity>` (from Tharga.Team) and their own `TeamFortnoxReaderCollection<TeamFortnoxView>` (lean projection that only reads the `FortnoxToken` field) against collection name `"TeamEntity"`. Same key → `ToDictionary` throws.

The dynamic-lookup right below already tolerates this via `GroupBy` + `.First()`. The static lookup didn't follow the same pattern.

## Scope

### Fix

Change the static-lookup construction to mirror the dynamic-lookup's group-and-take-first treatment, with one addition: merge `EntityTypes` across the group so the monitor UI shows all the readers' entity-type names for the shared physical collection:

```csharp
_staticLookup = GetStaticCollectionsFromCodeCore()
    .GroupBy(x => (x.ConfigurationName ?? _options.DefaultConfigurationName, x.CollectionName))
    .ToDictionary(
        g => g.Key,
        g => g.First() with { EntityTypes = g.SelectMany(x => x.EntityTypes).Distinct().ToArray() });
```

### Tests

Two unit tests against a small helper that isolates the lookup-building logic:

- `BuildStaticLookup_DropsDuplicateKey_AndMergesEntityTypes` — two `StatColInfo` records with the same `(config, coll)` produce one entry with the union of `EntityTypes`.
- `BuildStaticLookup_PreservesDistinctEntries` — different `(config, coll)` keys remain distinct.

## Out of scope

- The unused `var c = b.Where(x => x.Count() > 1).ToArray();` dead code immediately below the dynamic lookup. Could clean up but separate concern; keeps this PR focused.
- Any sorting rule for which `StatColInfo.First()` wins when there's a tie. Florida's case works either way — the canonical writer class happens to have `BuildIndexMetas()` populated, but the read-projection class doesn't override indices, so `DefinedIndices` are equivalent. Document the "first one wins" behaviour and revisit only if a real consumer needs a different ordering.

## Acceptance criteria

- `DatabaseMonitor` no longer throws when two static registrations overlay the same `(configurationName, collectionName)`.
- The resulting lookup entry surfaces both reader entity-type names via `EntityTypes`.
- Different `(config, coll)` keys remain independent.
- New tests pass; existing suite stays green (modulo the pre-existing Lockable cohort).

## Florida impact

Once shipped + Florida bumps to the new version, they can restore the proper `DiskRepositoryCollectionBase<TeamFortnoxView>` derivation on `TeamFortnoxReaderCollection`, retire the `IMongoDbServiceFactory` workaround in [`TeamFortnoxView.cs`](c:/dev/tharga/Florida/Tharga.Florida.Web/Features/Fortnox/TeamFortnoxView.cs), and `/developer/database` keeps working.

## Effort

Small. ~5-line code change + two tests.

## NuGet

Current as of 2.10.12. No bumps needed.
