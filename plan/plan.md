# Plan: sort/ordering control when acquiring a lock (#135)

Branch: `feature/pick-sort` (from `master`)

## Steps

- [x] 0. Start-of-feature checks
      `git fetch` — in sync with `origin/master`, nothing to pull.
      `dotnet outdated` across the solution — "No outdated dependencies were detected".
      No package upgrades to bundle into this PR.

- [x] 1. Add `PickOptions<TEntity>`
      `Tharga.MongoDB/Lockable/PickOptions.cs` — one `init`-only `SortDefinition<TEntity> Sort`,
      XML-documented as affecting selection, not the lock.

- [x] 2. Thread the sort through the acquire path
      `AcquireLockAsync` and `CreateLockAsync` both take `PickOptions<TEntity>` in position 2
      (required, not defaulted, so the compiler flags every call site). `AcquireLockAsync` now
      builds `new OneOption<TEntity> { Mode = EMode.FirstOrDefault, Sort = pickOptions?.Sort }`
      in place of the static `OneOption<TEntity>.FirstOrDefault`. Id-based and `WaitForLock`
      call sites pass `null`.

- [x] 3. Add the sorted overloads
      Six on the interface + six on the base class. The unsorted filter/predicate overloads
      now forward to the sorted ones with `null`, so there is one implementation per operation.

- [x] 4. Tests — sorted selection
      `Tharga.MongoDB.Tests/Lockable/PickSortTests.cs`, 18 test cases.

- [x] 5. Tests — ordering interaction with locking
      All five interaction cases covered. **Seeds are chosen so MongoDB's natural
      (insertion) order never coincides with the expected result** — an audit found 5 of the
      first-draft tests would have passed without the feature, and they were reseeded.
      Verified by temporarily forcing `Sort = null` in `AcquireLockAsync`: 13 of 18 failed,
      and the 5 that still passed are exactly the ones that must not depend on sort
      (null-options, null-sort, no-match).

- [x] 6. Build + full test suite
      Baseline on unmodified `master`: 693 passed, 6 failed, 8 skipped (707 total).
      After the feature: 712 passed, 5 failed, 8 skipped (725 total).
      The 5 remaining failures are the documented environmental `TransactionsTests`
      (standalone mongod, no replica set). The sixth baseline failure, the known-flaky
      `GetLockedExpired`, passed this run.

- [x] 7. Docs (`docs:` commit, before close-out)
      New "Choosing which document to lock" section in both `README.md` and
      `docs/articles/lockable-collections.md`, with the work-queue example, the
      skipped-locked / reclaimed-expired behaviour, the exclusions (`id` overloads,
      `LockManyAsync`) and an index note. The Pick-style section in each links to it.
      Two extra tests were added to back the documented concurrency claim rather than
      just assert it in prose: `ConcurrentSortedPicksTakeDistinctDocuments` and
      `SortedPickReturnsNullWhenEveryMatchIsLocked`. 20 tests total, stable over
      repeated runs.

- [ ] 8. Close-out (only when the user confirms the feature is done)
      Re-run `dotnet outdated`, archive `plan/feature.md` to the Plan directory `done/`,
      `git rm -r plan`, final `feat: pick sort complete` commit, push, open PR.

## Decisions

- **Options record over a plain `sort` parameter** — same overload count either way, but
  #136's per-group exclusivity likely wants another pick-time knob, and a property beats
  six more overloads. User chose this over the plain-parameter shape from the issue text.
- **`LockAsync` included** — user confirmed. Same `AcquireLockAsync` mechanism, same
  arbitrary-match problem; excluding it would just generate a follow-up issue.
- **Additive only** — the issue proposed inserting `sort` as parameter 2 of the existing
  signatures, which breaks positional callers. Tharga.MongoDB is consumed by Eplicta,
  Florida, Platform, Quilt4Net.Server and PlutusWave, so the sorted variants are new
  overloads instead. No version-compatibility break, so no minor bump needed on that account.
- **`OneOption.Mode` stays internal to the acquire path** — exposing it would let a caller
  select `Single`/`SingleOrDefault` and break the atomic `FindOneAndUpdate` guarantee.

## Known caveat

A caller passing a **literal `null` positionally** in argument position 2 —
`PickForUpdateAsync(filter, null)` — becomes ambiguous between the `TimeSpan? timeout`
and `PickOptions<TEntity> pickOptions` overloads, since neither is a better conversion
target for `null`. The fix at such a call site is to name the argument
(`timeout: null`). The whole solution, including samples and tests, compiles unchanged,
so nothing in this repo hits it. Worth a line in the release notes.

## Flaky test observed (not caused by this feature)

`DeleteManyTests.DeleteWhenOneIsExpired` failed on **one** full-suite run on this branch,
then passed on the next, passes in isolation, and did not fail on two full-suite runs on
unmodified `master`.

It is the same root cause as the already-documented `GetLockedExpired` flake: the test
picks with `TimeSpan.Zero` (so `ExpireTime == UtcNow`) and then requires
`ExpireTime < UtcNow` on the following call. If the clock does not tick between the two,
the document is not yet expired and the assertion fails.

This feature cannot affect it — the id-based `PickForUpdateAsync` path passes
`null` pick options, producing an `OneOption` identical to the previous static
`OneOption<TEntity>.FirstOrDefault`, and `DeleteManyUnlockedAsync` is untouched. What
changed is suite composition (20 new tests ahead of it), which shifts timing enough to
expose a latent race. Logged to the backlog next to `GetLockedExpired`.

## Last session

Steps 0-7 complete. Implementation, tests and docs are committed in three commits
(`feat:`, `test:`, `docs:`). Full suite is at baseline: 5 environmental transaction
failures only.
Next: push the branch for the user to test. Step 8 close-out (re-run `dotnet outdated`,
archive `feature.md`, `git rm -r plan`, final commit, PR) only on the user's confirmation.
