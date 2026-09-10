# Plan: MongoDB dependency spans (#157)

Branch: `feature/mongodb-dependency-spans`

## Steps

- [x] **1. Dependency update** — `dotnet outdated -u` across the solution, excluding `MongoDB.Driver`
      (held at 3.9.0 for Cosmos 4.2, #158). 14 packages bumped across 10 projects, all patch/minor.
      Build clean, test suite unchanged from baseline. Commit `f325ee4` — `chore(deps): nuget update`.

- [ ] **2. Additive cluster configuration**
  - `DatabaseOptions`: internal `_clusterConfigurators` list + public `ConfigureCluster(Action<ClusterBuilder>)`.
  - `MongoDbClientProvider`: replace the `settings.ClusterConfigurator = …` assignment with a composed
    delegate built from an ordered step list — existing configurator, built-in monitors, consumer callbacks.
  - Pass the options through the registration lambda in `MongoDbRegistrationExtensions`.

- [ ] **3. Tests for composition**
  - Consumer callbacks run in registration order, after the built-ins.
  - A pre-existing configurator is preserved.
  - Built-in monitor subscriptions survive a consumer callback (the regression guard from the issue).
  - The composed delegate runs against a real `ClusterBuilder` without throwing.

- [ ] **4. ActivitySource emitter**
  - Public `MongoDbDiagnostics.ActivitySourceName` const (`Tharga.MongoDB`).
  - Internal subscriber on `CommandStarted` / `CommandSucceeded` / `CommandFailed`, correlated by
    `RequestId`, with `HasListeners()` as the fast-path gate and the excluded-command set.
  - OTel semantic-convention tags; `Error` status + `error.type` on failure.
  - `MonitorOptions.EnableActivitySource` (default true) and `CaptureCommandText` (default false).
  - Register the subscriber and wire it into the built-in configurator step.

- [ ] **5. Tests for spans**
  - Name, kind and tags on success; error status and `error.type` on failure.
  - Parent correlation to an ambient `Activity`.
  - Excluded commands emit nothing.
  - `db.statement` absent by default, present under `CaptureCommandText`.
  - Zero allocation with no listener, plus a guard test so the zero-assertion cannot pass vacuously.
  - `EnableActivitySource = false` registers no subscriber.

- [ ] **6. Docs**
  - `docs/articles/monitoring.md`: new tracing section — the source name, the OTel registration snippet,
    the tag set, the two options, and the duplicate-span note about `ConfigureCluster`.
  - `README.md`: options table rows + a tracing subsection.

- [ ] **7. Version bump** — `MAJOR_MINOR` `2.16` → `2.17` in `.github/workflows/build.yml`.

- [ ] **8. Verify** — clean build at 0 warnings; full suite green apart from the 6 known failures.

- [ ] **9. Push and hand over for testing** — do not open the PR yet.

## Close-out (only once the user confirms the feature is done)

- [ ] Re-run `dotnet outdated` across the solution and apply what has published since step 1.
- [ ] `Requests.md` — no `## Tharga.MongoDB` row exists for this; add one recording what shipped, or record
      it against the upstream row, per whichever the close-out sweep finds.
- [ ] `upstream-requests.md` — the Quilt4Net Server → Tharga/MongoDB #157 row: mark
      `closed-awaiting-consumption` with the shipping version, since consumption is their side.
- [ ] Comment on #157 with the version, the source name and the registration snippet, then close it.
- [ ] Archive `plan/feature.md` to `$DOC_ROOT/Tharga/plans/Toolkit/MongoDB/done/mongodb-dependency-spans.md`.
- [ ] `git rm -r plan`, final commit `feat: mongodb dependency spans complete`, push, open PR.

## Last session

2026-09-10 — Branch created off `master` (level with `origin/master`, tagged 2.16.0). Baseline captured:
741 tests, 727 passed, 8 skipped, 6 failed (all pre-existing and recorded in the backlog). Step 1 done and
committed. Awaiting plan confirmation before step 2.
