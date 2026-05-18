# Feature: Index-failure telemetry

References Florida's request in `Requests.md` (2026-05-13). Implements Asks #1 and #3 of three; Ask #2 (cross-process persistence) is deferred to a planned follow-up.

## Goal

Stop turning every Azure App Service restart on Florida into 58 AppExceptions entries for the same already-known index conflict, and give consumers a clean API to find out *which* indexes are blocked so they can build admin UIs without writing collection-specific auditors.

## Background

`DiskRepositoryCollectionBase`'s three index-update methods (`UpdateIndicesByNameAsync`, by-schema, `UpdateIndicesByDropCreateAsync`) all share the same pattern: on `DropOneAsync` / `CreateOneAsync` failure, they call `_logger.LogError(ex, ...)` and `_initiationLibrary.AddFailedInitiateIndex(...)`. Six catch sites total.

Florida prod hits one such failure 58×/day because:

- The collection has a real data conflict (a unique partial index can't be built — historical duplicate).
- Every process restart re-runs the index sync, hits the same conflict, re-logs the same Error.
- OTel promotes the Error+exception into the AppExceptions table on every restart.
- The `_initiationLibrary` already knows about the failure but doesn't influence the log severity — and doesn't expose itself to consumers who'd otherwise build an audit UI.

## Scope

### 1. Severity downgrade on retry (Ask #1)

A small helper inside `DiskRepositoryCollectionBase` checks `_initiationLibrary.GetFailedIndices(...)` before logging:

- First occurrence per process for a given `(operation, indexName)` → `LogError(ex)` (existing behaviour, captures the cause once).
- Subsequent occurrences → `LogWarning` without the exception attached.

Applied at all 6 catch sites. The recording into `_initiationLibrary.AddFailedInitiateIndex` is unchanged.

### 2. Public `GetFailedIndices()` API (Ask #3, metadata version)

- New public `IndexFailure` record carrying `Operation` (Create / Drop), `Name`, `LastErrorMessage`.
- New `GetFailedIndices()` method on `IDiskRepositoryCollection<TEntity>` returning `IReadOnlyList<IndexFailure>` for this collection's failed indexes.
- Sync, not async — data is in-memory in `InitiationLibrary`.

### 3. Latent dedup bug

`InitiationInfo.FailedIndices` is currently a `List<(IndexFailOperation, string)>`. Same `(op, name)` hitting twice (e.g. across retries before #1's downgrade decision changes anything) gets duplicated. Switching to `HashSet` makes `AddFailedInitiateIndex` idempotent and the "already-known?" check O(1). Captured message uses the *latest* one (overwrite-on-add semantics).

## Out of scope

- **Ask #2 — cross-process persistence.** Adds a `_indexInitiationState` collection (or TTL'd doc), surfaces schema decisions, and is more invasive. Florida's stated condition for retiring their workaround is "Asks 1+3 ship"; Ask 2 stays optional. Recorded as `planned/index-failure-persistence.md`.
- **Ask #3 server-side conflict-doc query.** Synthesising aggregations from index definitions (partial filters, compound keys, etc.) is substantial. Florida keeps their `CardPaymentIndexConsistencyAuditor` to enumerate the actual conflicting documents and uses our metadata to know *which* indexes are blocked. Recorded as `planned/index-conflict-doc-discovery.md`.

## Acceptance criteria

- A given `(operation, indexName)` failure in the same process logs at `Error` exactly once and at `Warning` for every subsequent occurrence.
- The exception is attached to the first log entry, not to subsequent warnings.
- `IDiskRepositoryCollection<TEntity>.GetFailedIndices()` returns one entry per failed index this process has tried for this collection, with `Operation`, `Name`, and the most recent `LastErrorMessage`.
- Recording the same `(op, name)` twice doesn't duplicate the entry.
- Existing tests stay green; new tests cover both phases.

## Done condition

- Acceptance criteria met.
- Two follow-ups filed in `planned/` (persistence + conflict-doc discovery) so the deferred work stays visible.
- PR opened, merged. Plan archived to `done/`.

## Validation

Unit tests are sufficient. Eplicta smoke optional — no behaviour relevant to Eplicta changed.

## NuGet

`Tharga.Communication` 0.2.0 and the rest are current from PRs #104 and #105. No bumps needed.
