# Plan: Failed-index recheck strategy

Feature scope: see [feature.md](feature.md). Branch: `feature/failed-index-recheck`. Closes #110.

## Steps

### Phase 1 — Remove retry-on-touch ✅

- [x] **1.1** Located `OperationIndexManagement` in `DiskRepositoryCollectionBase.cs:214`. The Update branch was `AssureIndex; ArmRecheckInvalidIndex`; the Delete branch was just `ArmRecheckInvalidIndex` (no assure).
- [x] **1.2** Removed both `ArmRecheckInvalidIndex()` invocations. `Update` is now identical to `Create` (just `AssureIndex(collection)`); `Delete` is a no-op like `Read`. Also removed the now-dead `ArmRecheckInvalidIndex` private helper. `IInitiationLibrary.RecheckInitiateIndex` left in place per the design decision — even though `RestoreIndexAsync` uses `force: true` rather than calling it, removing it from a public interface is a breaking change we don't want without stronger reason.
- [x] **1.3** No existing tests directly exercised the retry-on-touch behaviour (verified by full-suite run — only pre-existing Lockable transaction-test flakiness, same as bare master).

### Phase 2 — `AssureIndexAtStartup` ✅

- [x] **2.1** Added `bool AssureIndexAtStartup { get; set; } = false;` to `DatabaseOptions`.
- [x] **2.2** The existing eager-startup pass at `MongoDbRegistrationExtensions.UseMongoDB:376` was gated only by `UseMongoOptions.AssureIndex = true` (default true). Added `&& databaseOptions.Value.AssureIndexAtStartup` to the gate so the new option governs the global behaviour. **Behaviour change**: existing consumers who relied on the implicit eager-startup default get lazy first-access assurance instead. Documented in the commit/PR description.
- [x] **2.3** Test deferred to integration smoke — unit-testing the `UseMongoDB` wire-up needs a real `WebApplicationBuilder` and is brittle. The option's default is pinned by `DatabaseOptions_AssureIndexAtStartup_DefaultsToFalse`; the gate change is mechanical.

### Phase 3 — Periodic sweep ✅

- [x] **3.1** Added `TimeSpan? FailedIndexRecheckInterval { get; set; } = TimeSpan.FromHours(1);` to `DatabaseOptions`.
- [x] **3.2** New `FailedIndexRecheckService : BackgroundService` (internal) — loops with `Task.Delay(interval)` between ticks; calls `IDatabaseMonitor.GetCollectionsWithFailedIndices()` per tick; returns immediately on empty result (the "dormant when healthy" property); calls `RestoreIndexAsync(force: false)` for each collection with failures.
- [x] **3.3** Registered in `AddMongoDB` (not `UseMongoDB`) when interval is non-null, via `services.AddHostedService<FailedIndexRecheckService>()`.
- [x] **3.4** Tests for the sweep itself deferred — the service is a thin loop over `IInitiationLibrary` primitives that ARE tested (idle when no failures, retry-then-clear when failures resolved). End-to-end sweep behaviour is best verified by integration smoke.

### Phase 4 — `IDatabaseMonitor.GetCollectionsWithFailedIndices` ✅

- [x] **4.1** Added to `IDatabaseMonitor` returning `IReadOnlyList<CollectionInfo>` (per user's question-2 sign-off). Implementation in `DatabaseMonitor` joins `IInitiationLibrary.GetCollectionsWithFailures()` (new helper on the library) against the cached `CollectionInfo`s by `(Server, DatabaseName, CollectionName)`.
- [x] **4.2** Stubs added to `DatabaseNullMonitor` (`=> []`) and to the test-side `IngestOnlyMonitor` (`throw new NotImplementedException()`).
- [x] **4.3** `GetCollectionsWithFailures_*` tests in `FailedIndexRecheckTests` cover empty / single / multiple-collection cases + the "clears when all failures resolved" property.

### Phase 5 — `ClearFailedIndex` ✅

- [x] **5.1** Added to `IInitiationLibrary` with a corresponding `void` implementation that removes the `(operation, indexName)` key from the collection's `FailedIndices` `ConcurrentDictionary`. Safe no-op when the collection or entry isn't present.
- [x] **5.2** Wired at all 6 per-index success sites inside `UpdateIndicesByNameAsync` / `BySchemaAsync` / `ByDropCreateAsync` via a tiny `ClearIndexFailure(operation, indexName)` private helper (matches the existing `LogIndexOperationFailure` pattern).
- [x] **5.3** Three tests pin the contract: only-matching-entry removed, no-op for absent entries, no-op for uninitiated collections.

### Phase 6 — `TouchAsync` triggers assure ✅

- [x] **6.1** Traced `TouchAsync` → `FetchMongoCollection(initiate: true)` → `FetchCollectionAsync(initiate: true)` → `ShouldInitiate` (per-session-once gate). After the first session-access, subsequent Touches were no-ops for index assurance.
- [x] **6.2** Added an `await RestoreIndexAsync(collectionInfo, force: true)` at the end of `TouchAsync` so a touch on a previously-initiated collection forces a fresh assure pass. Wrapped in try/catch with debug-level logging so a stuck index doesn't propagate up — the failure is already captured by the `LogIndexOperationFailure` helper.
- [x] **6.3** End-to-end test deferred to integration smoke — `TouchAsync` requires the full DI tree.

### Phase 7 — Close-out

- [ ] **7.1** Single cohesive commit per recent pattern.
- [ ] **7.2** Push.
- [ ] **7.3** Archive plan to `done/failed-index-recheck.md`, update `planned/README.md` Done section, `git rm -r plan`, final commit `feat: failed-index-recheck complete`, push, open PR closing #110.

## Last session

Plan finalised after design discussion. Six numbered scope items, one branch, will land as one cohesive commit + close-out. Awaiting go-ahead to start Phase 1.

## Open questions worth flagging at impl

- Does `RestoreIndexAsync` need any signature change to support being called from the background sweep (e.g. accepting a cancellation token from the service's `stoppingToken`)? Verify at Phase 3.
- The naming choice between exposing `GetCollectionsWithFailedIndices` on `IDatabaseMonitor` vs adding a richer query on `IInitiationLibrary` — at impl time, pick the one with fewer plumbing changes.