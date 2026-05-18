# Plan: Index-failure telemetry

Feature scope: see [feature.md](feature.md). Branch: `feature/index-failure-telemetry`.

## Steps

### Phase 1 — Severity downgrade + state changes ✅

- [x] **1.1** `InitiationInfo.FailedIndices` reshaped from `List<(Operation, Name)>` to `ConcurrentDictionary<(Operation, Name), string>` — key carries the (op, name), value holds the latest error message.
- [x] **1.2** `IInitiationLibrary.AddFailedInitiateIndex` extended to take `(IndexFailOperation operation, string indexName, string errorMessage)` — flattened parameter shape now that the value tuple grew.
- [x] **1.3** Added `LogIndexOperationFailure(IndexFailOperation, string, Exception)` private helper on `DiskRepositoryCollectionBase`. First occurrence per (op, name) logs `Error` with exception; subsequent occurrences log `Warning` without exception. Always records via the extended `AddFailedInitiateIndex`.
- [x] **1.4** All 6 catch sites in `DiskRepositoryCollectionBase` replaced with `LogIndexOperationFailure(...)` calls — `UpdateIndicesByNameAsync`, the by-schema method, and `UpdateIndicesByDropCreateAsync` × 2 sites each.
- [x] **1.5** `GetFailedIndices` on `IInitiationLibrary` now returns `IReadOnlyList<IndexFailure>` directly — no more tuple shape internally.

### Phase 2 — Public `GetFailedIndices()` API ✅

- [x] **2.0** Discovered mid-implementation: `GetFailedIndices()` was **already public** on `IDiskReadOnlyRepositoryCollection`, returning `IEnumerable<(Operation, Name)>` tuples. So Phase 2 became *enriching the existing return type* to `IReadOnlyList<IndexFailure>` (with the `LastErrorMessage` field). Minor breaking change for any consumer using the tuple shape — fixed in the in-repo `WeatherForecastRepository` sample.
- [x] **2.1** New public `IndexFailure` record with `Operation`, `Name`, `LastErrorMessage`, in `Tharga.MongoDB`.
- [x] **2.2** `IDiskReadOnlyRepositoryCollection.GetFailedIndices()`, `RepositoryCollectionBase.GetFailedIndices()`, `DiskRepositoryCollectionBase.GetFailedIndices()`, `LockableRepositoryCollectionBase.GetFailedIndices()` all migrated to the new return type. Sample `IWeatherForecastRepository` + `WeatherForecastRepository` also updated.
- [x] **2.3** XML docs on the interface explain it's an in-process view, not cross-process, with a forward-pointer to the planned persistence follow-up.

### Phase 3 — Tests ✅

- [x] **3.1** 8 tests in `InitiationLibraryTests` covering:
  - empty/no-state safety (`GetFailedIndices` doesn't throw on uninitiated collection)
  - basic record + retrieve
  - idempotency on same (op, name)
  - latest-error-message overwrite
  - (Create, Name) and (Drop, Name) are distinct entries
  - `RecheckInitiateIndex` resets the assured flag only when there are failures to retry
- [x] **3.2** Direct unit test of the private `LogIndexOperationFailure` helper skipped per the plan's fallback ("if the integration angle is too brittle"). Mocking a `Mongo.Driver` collection to throw on `CreateOneAsync` from inside `DiskRepositoryCollectionBase`'s wire-up tree was the brittle route. The helper's logic is a 5-line conditional that consumes `InitiationLibrary` output (which IS thoroughly tested), so the residual risk is review-checkable.
- [x] **3.3** Public-`GetFailedIndices()` behaviour covered by Phase 3.1 — the in-collection method is a thin pass-through to the library.

Full suite: 448 passed (up from 440 — 8 new). 5 Lockable failures match the pre-existing flaky cohort on bare master.

### Phase 4 — Follow-up specs + close-out

- [ ] **4.1** Write `planned/index-failure-persistence.md` (Ask #2).
- [ ] **4.2** Write `planned/index-conflict-doc-discovery.md` (the conflict-doc query side of Ask #3).
- [ ] **4.3** Update `planned/README.md` to include both.
- [ ] **4.4** Full test pass.
- [ ] **4.5** Single commit (per recent project pattern), push, close-out commit removing `plan/`, push, PR.

## Last session

Plan written. Awaiting user confirmation before code.

## Phase 1 results

_To be filled in._
