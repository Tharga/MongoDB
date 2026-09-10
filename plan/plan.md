# Plan: MongoDB dependency spans (#157)

Branch: `feature/mongodb-dependency-spans`

## Steps

- [x] **1. Dependency update** — `dotnet outdated -u` across the solution, excluding `MongoDB.Driver`
      (held at 3.9.0 for Cosmos 4.2, #158). 14 packages bumped across 10 projects, all patch/minor.
      Build clean, test suite unchanged from baseline. Commit `f325ee4` — `chore(deps): nuget update`.

> Steps 2 and 4 were done before 3 and 5. Adding the option surface up front kept every intermediate build
> at zero warnings — `ConfigureCluster`'s XML docs reference `EnableActivitySource`, so writing the hook
> first left a dangling `cref`. No change to what any step contains.

- [x] **2. Additive cluster configuration** — `DatabaseOptions._clusterConfigurators` + public
      `ConfigureCluster(Action<ClusterBuilder>)`. `MongoDbClientProvider.BuildConfiguratorSteps` composes an
      ordered `ClusterConfiguratorStep` list (Existing → BuiltIn → Consumer) and `GetClient` runs it, in place
      of the old assignment. Registration passes `o._clusterConfigurators` through.

- [x] **3. Tests for composition** — `ClusterConfiguratorCompositionTests`, 7 tests: step order with and
      without an existing configurator, consumer callbacks in registration order, the built-in step surviving
      a consumer callback, no steps when there is nothing to configure, every step against a real
      `ClusterBuilder`, and `GetClient` applying the composition to a real `MongoClient`.

- [x] **4. ActivitySource emitter** — public `MongoDbDiagnostics.ActivitySourceName`; internal
      `CommandActivityEmitter` on `CommandStarted`/`Succeeded`/`Failed`, correlated by `RequestId`, gated on
      `HasListeners()`, with the handshake/heartbeat exclusion set and the OTel tag set.
      `EnableActivitySource` (default true) and `CaptureCommandText` (default false) on `MonitorOptions`.
      Subscribed inside the built-in configurator step, so it always precedes consumer callbacks.

- [x] **5. Tests for spans** — `CommandActivityEmitterTests` (17) and `ActivitySourceRegistrationTests` (5):
      kind/name/tags, database-level commands reporting no collection, error status and `error.type`, parent
      correlation to an ambient activity, two in-flight commands not colliding, six excluded commands,
      `db.statement` both ways, zero allocation with no listener plus two guards against a vacuous zero, and
      the registration switch from both code and configuration.

- [x] **6. Docs** — both surfaces. `docs/articles/monitoring.md`: two new option rows plus *Dependency spans
      (distributed tracing)* and *Attaching your own driver subscriber* sections. `README.md`: the two options
      in both the appsettings and code samples, plus matching sections. Each explains why this option defaults
      on where `EnableCommandMonitoring` defaults off, and warns about duplicate spans.

- [x] **7. Version bump** — `MAJOR_MINOR` `2.16` → `2.17` in `.github/workflows/build.yml`.

- [x] **8. Verify** — solution builds at 0 warnings; 770 tests, 757 passed, 8 skipped, 5 failed, the 5 being
      the environmental `TransactionsTests`.

- [~] **9. Push and hand over for testing** — do not open the PR yet.

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
741 tests, 727 passed, 8 skipped, 6 failed (all pre-existing and recorded in the backlog). Steps 1–5 done
and committed. Suite now 770 tests, 757 passed, 8 skipped, 5 failed — the 5 being the environmental
`TransactionsTests` that need a replica set; the flaky `DeleteWhenOneIsExpired` passed this run. Solution
builds at 0 warnings. Steps 6–8 then landed: docs on both surfaces and the 2.17 version bump, re-verified
at 770 tests / 0 warnings. Implementation is complete; awaiting the user's test of the pushed branch before
the close-out sequence.
