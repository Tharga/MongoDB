# Feature: CancellationToken on the write methods (#166)

Branch: `feature/write-cancellation-tokens`. Ships as **2.19** (binary-breaking signatures).

## Decision (user, 2026-10-25)

Add a trailing `CancellationToken cancellationToken = default` to the existing write signatures, the same way the
reads take it, rather than adding overloads. One method each, so recompiled callers and their Moq setups keep
working. Binary break: a package built against 2.18 that calls these methods (Tharga.Team.MongoDB,
Tharga.Cache.MongoDB) throws `MissingMethodException` until rebuilt, hence the minor bump and a cascade rebuild.
Rejected: overloads (consumers' Moq setups on the old overload stop matching after a recompile; 17 extra interface
members) and token-before-session (inconsistent with the reads).

## Scope

- `IRepositoryCollection`: `AddAsync`, `TryAddAsync`, `AddManyAsync`, `DeleteOneAsync(id)`.
- `IDiskRepositoryCollection`: `AddOrReplaceAsync`, `ReplaceOneAsync`, three `UpdateOneAsync`, two `UpdateManyAsync`,
  two `DeleteOneAsync`, two `DeleteManyAsync`; plus the class-only `ReplaceOneAsync(entity, filter, ...)`.
- `LockableRepositoryCollectionBase`: the add overrides forward the token; `DeleteOneAsync(id)` checks it once before
  the pick (cancelling between pick and commit would leave the document locked until it expires).
- Out of scope: lockable picks, locks, releases and the `*Unlocked` methods.
- Found on the way, fixed: `UpdateOneAsync(predicate, update, options, session)` dropped `options`.

## Acceptance criteria
- Every write above, called with a cancelled token, throws `OperationCanceledException` and writes nothing.
- Lockable `DeleteOneAsync` with a cancelled token throws before locking.
- `UpdateOneAsync(predicate, ..., options)` honours `options`.
- `MAJOR_MINOR` 2.19; README/docs describe the token and the upgrade note.
