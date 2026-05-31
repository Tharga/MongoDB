# Plan: Static-lookup dedup

Feature scope: see [feature.md](feature.md). Branch: `feature/static-lookup-dedup`.

## Steps

### Phase 1 — Extract testable helper + apply fix ✅

- [x] **1.1** Added `internal static DatabaseMonitor.BuildStaticLookup(IEnumerable<StatColInfo>, string defaultConfigurationName)` returning the deduped `Dictionary<(string, string), StatColInfo>`. Internal so tests can call it directly via InternalsVisibleTo.
- [x] **1.2** Replaced the failing `.ToDictionary(...)` call in `GetLookups` with `BuildStaticLookup(...)`.
- [x] **1.3** Build clean (0 warnings).

### Phase 2 — Tests ✅

- [x] **2.1** Three tests in `DatabaseMonitorStaticLookupTests`:
  - `BuildStaticLookup_DropsDuplicateKey_AndMergesEntityTypes`
  - `BuildStaticLookup_PreservesDistinctEntries`
  - `BuildStaticLookup_AppliesDefaultConfigurationName_WhenStatColInfoHasNullConfiguration` (the null-config edge case that the original `?? _options.DefaultConfigurationName` was handling)
- [x] **2.2** Full suite: 451 passed (up from 448, 3 new). Same Lockable cohort as bare master.

### Phase 3 — Close-out

- [ ] **3.1** Commit (single, per recent pattern).
- [ ] **3.2** Push.
- [ ] **3.3** Archive plan to `done/static-lookup-dedup.md`, update `planned/README.md`, `git rm -r plan`, final commit `fix: static-lookup-dedup complete`, push, open PR referencing Florida's request.

## Last session

Plan written. Awaiting confirmation before code.
