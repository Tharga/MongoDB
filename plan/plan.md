# Plan: Stable schema fingerprint that survives read-only properties

Feature scope: see [feature.md](feature.md). Branch: `feature/fingerprint-readonly-properties`.

## Steps

### Phase 1 — Reproduce ✅

- [x] **1.1** New test file `SchemaFingerprintTests.cs` with five fixture types covering read-only auto, init-only, computed get-only, hidden-via-`new`, and plain inheritance.
- [x] **1.2** Stability tests pass for all fixtures (100 runs each). Hypothesis A does not reproduce.
- [x] **1.3** Schema-honesty tests reproduce hypothesis B in three of five fixtures (read-only auto, computed get-only, hidden inheritance). Required `BsonClassMap.Freeze()` after `AutoMap()` to populate `AllMemberMaps` correctly.
- [x] **1.4** Results documented below.

### Phase 2 — Fix the algorithm

- [ ] **2.1** Based on Phase 1, pick the fix:
  - If only **A**: replace `OrderBy(p.Name)` with `OrderBy(p.Name).ThenBy(p.DeclaringType.FullName)` to get a stable secondary key for hidden properties.
  - If **B** (likely per backlog note): switch `SchemaFingerprint.Generate` to walk `BsonClassMap.AutoMap(type).AllMemberMaps` (or `BsonClassMap.LookupClassMap` for pre-registered types). Each member's `MemberName` + `MemberType.FullName` becomes the hash input. Matches actual serialization semantics.
  - If both: do **B** (it incidentally fixes **A** because `BsonClassMap` deduplicates members).
- [ ] **2.2** Preserve the `FlexibleGuidAttribute` carve-out — it's a real attribute that affects on-disk representation, not just C# shape.
- [ ] **2.3** Add an algorithm-version prefix to the returned string (e.g. `v2:` followed by the hex hash) so old vs new fingerprints are visibly distinguishable. Old fingerprints (without prefix) are treated as "different" until the next clean — this is the stale-cache mitigation called out in feature.md scope item #3.

### Phase 3 — Stale-cache mitigation ✅

- [x] **3.1** Verified `FingerprintMatch` in `CollectionDialog.razor:129` is a plain `==` — handles versioned strings unchanged.
- [x] **3.2** **The prefix alone wasn't enough.** Two sites in `DatabaseMonitor` had a `existing.CurrentSchemaFingerprint ?? Compute(...)` short-circuit that would have kept an *old-algorithm* hash forever, never letting the new algorithm run on next refresh. Fixed:
  - Added `SchemaFingerprint.IsCurrentVersion(string)` — returns true only for strings prefixed with the current algorithm marker.
  - [`DatabaseMonitor.cs:400`](../Tharga.MongoDB/DatabaseMonitor.cs) (`RefreshStatsAsync` update factory) — now recomputes when the cached fingerprint is from an older algorithm.
  - [`DatabaseMonitor.cs:1383`](../Tharga.MongoDB/DatabaseMonitor.cs) (cache-load enrichment) — same fix.
- [x] **3.3** New `[Theory]` test pins `IsCurrentVersion` for null / empty / unprefixed hex / `v1:` prefix / `v2:` prefix.

The combined effect: collections cleaned before this PR retain their old `Clean.SchemaFingerprint` until the next clean. On the *next* monitor refresh after upgrade, the cached `CurrentSchemaFingerprint` recomputes to the v2 algorithm. The dialog shows "Outdated" once. After the next clean, both sides are v2 → "Current". This is the documented one-time cost.

### Phase 4 — Tests + regression

- [ ] **4.1** All Phase 1 tests now pass with the new algorithm. Phase 1.3 schema-honesty test in particular asserts parity with `BsonClassMap.AutoMap`.
- [ ] **4.2** Add a "clean → fingerprint match" end-to-end test if not already covered — call `CleanCollectionAsync`, read back `CleanInfo` and the cached `CollectionInfo`, assert match.
- [ ] **4.3** Full `dotnet test -c Release` pass (allowing for the pre-existing Lockable flakiness noted on the previous feature).

### Phase 5 — NuGet patch bundle

- [ ] **5.1** Bump packages on all six projects to:
  - `Microsoft.Extensions.*` 10.0.7 → 10.0.8
  - `MongoDB.Driver` 3.8.0 → 3.8.1
  - `Tharga.Runtime` 0.1.11 → 0.1.12
  - `Microsoft.SourceLink.GitHub` 10.0.203 → 10.0.300
  - `Tharga.Test.Toolkit` 1.14.8 → 1.14.9 (test project only)
  - `FluentAssertions` 8.9.0 → 8.10.0 (test project only)
- [ ] **5.2** `dotnet restore` + full build + full test. Commit as a separate logical commit so the bump is easy to revert if it surfaces something.

### Phase 6 — Documentation

- [ ] **6.1** README — short note under the Clean section: "Upgrading to this version invalidates existing schema fingerprints once; collections will display as Outdated until the next clean." Skip if Phase 2.3 ended up unnecessary (i.e., the old and new algorithms happen to produce identical hashes for typical types).
- [ ] **6.2** Backlog `MongoDB.md` — remove the "After clean..." entry.

### Phase 7 — Close out

- [ ] **7.1** Commit logical milestones (fingerprint fix + tests, NuGet bumps, README).
- [ ] **7.2** Push branch, user validation.
- [ ] **7.3** Close-out: archive `plan/feature.md` to `done/fingerprint-readonly-properties.md`, `git rm -r plan`, final commit `fix: fingerprint-readonly-properties complete`, push, open PR.

## Last session

Plan written. No code yet. Awaiting user confirmation before starting Phase 1.

## Phase 1 results

Run on net10.0 master branch (`Tharga.MongoDB`, current SchemaFingerprint algorithm).

**Hypothesis A — non-determinism**: ❌ does not reproduce. All 5 stability tests pass (100 iterations × 5 fixtures). `GetProperties` + LINQ `OrderBy` happen to be stable enough on this runtime even for hidden-via-`new` properties. Not the root cause of the user's bug.

**Hypothesis B — schema disagreement with `BsonClassMap.AutoMap`**: ✅ reproduces in three shapes:

| Fixture | Fingerprint sees | `BsonClassMap.AutoMap+Freeze` serializes | Verdict |
|---|---|---|---|
| `ReadOnlyAutoFixture` (`{ get; }` with no matching ctor convention) | `Id`, `Name` | `Id` only | Fingerprint records a property the BSON document never carries. |
| `ComputedGetOnlyFixture` (`=> expr`) | `First`, `Full`, `Last` | `First`, `Last` | Same shape — fingerprint pollutes the hash with a synthesized property. |
| `DerivedHidingFixture` (`new` hiding base property) | both copies of the hidden property | one entry | Fingerprint double-counts; BsonClassMap correctly deduplicates. |
| `DerivedInheritedFixture` (plain inheritance) | matches | matches | Not a problem. |
| `InitOnlyFixture` (`{ get; init; }`) | matches | matches | Init counts as settable — both agree. |

**Conclusion**: switch the algorithm to use `BsonClassMap.AutoMap` for member enumeration. That single change fixes all three failing shapes simultaneously and makes the fingerprint match what's actually on disk.

**Phase 2.3 versioning still required**: existing `Clean.SchemaFingerprint` and `CurrentSchemaFingerprint` records were written by the old algorithm. After this PR ships, any collection cleaned before upgrade shows "Outdated" exactly once until the next clean — documented one-time cost.
