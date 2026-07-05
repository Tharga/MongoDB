# Plan: #133 monitor NRE + #132 lockable Lock seam (one PR)

Branch: `fix/monitor-nre-and-lockable-seam` (PR → `master`)

## Steps

- [x] 1. **Package updates up front (mandatory).** Bumped `Tharga.Blazor` 2.2.0 → 2.2.1 in
      `Tharga.MongoDB.Blazor`. Build `-c Release` clean (0 warnings). Test suite: 629 passed,
      8 skipped, 5 failed — all 5 are `TransactionsTests` failing at
      `EnsureTransactionsAreSupported()` because the local mongod is standalone (transactions need
      a replica set). Pre-existing environmental, unrelated to the bump; CI validates against a
      replica set.
- [~] 2. **#133 test first.** Unit test for `IndexMetaConverter.BuildIndexMetas` guard
      (null instance → empty, no throw). Check whether the `GetDynamicRegistrations` skip path is
      reachable via existing `DatabaseMonitor*Tests`; if not without heavy infra, cover at the
      converter level and note why.
- [ ] 3. **#133 fix.** `GetDynamicRegistrations`: null cast → Debug log + `continue` (mirror
      static path). Defensive null-guard in `IndexMetaConverter.BuildIndexMetas`/`ResolveProperty`.
- [ ] 4. **#133 verify.** Full `dotnet test -c Release` green.
- [ ] 5. **#132 impl.** Make `Lock` ctor `public`; add public `WithLock` extension in
      `LockableEntityBaseExtensions`. Entity `Lock` property stays internal.
- [ ] 6. **#132 tests.** Construct `Lock`, `WithLock`, read via `GetLockInfo()`, and drive
      `SetErrorStateAsync` via `EntityScopeBuilder.Build(...)` — all in-memory, no mongod.
- [ ] 7. **#132 version bump.** Additive public API → minor version bump (check csproj/CI stamping).
- [ ] 8. **Docs.** Update `docs/articles/lockable-collections.md` (+ README if relevant) for the new
      construction seam. #133 is an internal fix → no consumer docs; state so. Add the
      "surface instance on CollectionAccessEventArgs" follow-up to `planned/README.md`.
- [ ] 9. **Full suite** `dotnet test -c Release` green; re-check `dotnet outdated`.
- [ ] 10. **Close-out.** Archive `plan/feature.md` → Plan directory `done/`, `git rm -r plan`,
      final commit, push, open the single PR.

## Notes
- Branch cut from local master, carries the `.claude/settings.json` permission-cleanup commit
  (`084005e`) — benign tooling config; will ride in this PR.
- PR mixes `fix:` (#133) and `feat:` (#132); use separate commits per part, neutral PR title.

## Last session
Not started — plan confirmed for combined PR; beginning step 1 (package bump).
