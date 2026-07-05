# Plan: #133 monitor NRE + #132 lockable Lock seam (one PR)

Branch: `fix/monitor-nre-and-lockable-seam` (PR → `master`)

## Steps

- [x] 1. **Package updates up front (mandatory).** Bumped `Tharga.Blazor` 2.2.0 → 2.2.1 in
      `Tharga.MongoDB.Blazor`. Build `-c Release` clean (0 warnings). Test suite: 629 passed,
      8 skipped, 5 failed — all 5 are `TransactionsTests` failing at
      `EnsureTransactionsAreSupported()` because the local mongod is standalone (transactions need
      a replica set). Pre-existing environmental, unrelated to the bump; CI validates against a
      replica set.
- [x] 2. **#133 test first.** Added `IndexMetaConverterTests.BuildIndexMetas_NullInstance_ReturnsEmpty`.
      The `GetDynamicRegistrations` skip path needs a full DatabaseMonitor + decorated-provider
      harness (11-dep ctor) to exercise — deferred to a possible follow-up; the converter-level
      guard covers the exact NRE site from the issue's stack trace.
- [x] 3. **#133 fix.** `GetDynamicRegistrations`: null cast → Debug log + `continue` (mirrors the
      static path). Defensive null-guard added to `IndexMetaConverter.BuildIndexMetas`.
- [x] 4. **#133 verify.** Converter + monitor lookup tests green (8/8). Full suite run deferred to
      step 9 (after #132).
- [x] 5. **#132 impl.** Made `Lock` ctor `public`; added public `WithLock<T> where T : LockableEntityBase`
      extension (symmetric with existing `GetLockInfo`). Entity `Lock` property stays internal.
- [x] 6. **#132 tests.** `LockConstructionSeamTests` (5 tests): ctor, WithLock+GetLockInfo round-trip
      with field preservation, WithLock(null), null-entity guard, and SetErrorStateAsync via
      `EntityScopeBuilder.Build(...)` — all in-memory, no mongod. Green.
- [x] 7. **#132 version.** No bump needed: `MAJOR_MINOR` is already `2.13` (unreleased minor; latest
      tag is 2.11.3). Additive API rides into the upcoming 2.13.0.
- [x] 8. **Docs.** Added "Inspecting and seeding lock state" section to
      `docs/articles/lockable-collections.md` (GetLockInfo + WithLock + EntityScopeBuilder test
      pattern). #133 is an internal fix → no consumer docs. Added the
      "surface instance on CollectionAccessEventArgs" follow-up to `planned/README.md`.
      README check below.
- [x] 9. **Full suite** `dotnet test -c Release`: 634 passed, 8 skipped. Failures are NOT from this
      change: 5 fixed `TransactionsTests` (need a replica set; local mongod is standalone) + one
      *rotating* timing-flaky test under parallel load (run A → `GetLockedExpired`, run B →
      `RevalidationQueueTests.HighPriorityKeys_DrainBeforeLow`; each passes in isolation). The 6 new
      tests pass in every run. `dotnet outdated`: no outdated dependencies.
- [~] 10. **Close-out.** Commit fix/feat/docs, then archive `plan/feature.md` → Plan directory
      `done/`, `git rm -r plan`, final commit, push, open the single PR.

## Notes
- Branch cut from local master, carries the `.claude/settings.json` permission-cleanup commit
  (`084005e`) — benign tooling config; will ride in this PR.
- PR mixes `fix:` (#133) and `feat:` (#132); use separate commits per part, neutral PR title.

## Last session
Implementation complete for both #133 and #132. Commits on branch:
- `chore:` Tharga.Blazor 2.2.1 bump + plan
- `fix:` #133 monitor NRE guard (DatabaseMonitor skip + IndexMetaConverter null-guard + test)
- `feat:` #132 public Lock ctor + WithLock extension (+ in-memory seam tests)
- `docs:` GetLockInfo/WithLock seam in README + lockable-collections.md
Verification: build clean (0 warnings); 634 passed / 8 skipped; the only failures are the 5
environmental TransactionsTests (replica set) + one rotating timing-flaky test (passes in
isolation). No outdated packages.

Next: await user approval to push the branch for testing, then close out (archive feature.md →
done/, `git rm -r plan`, final "complete" commit) and open the single PR → master.
