# Feature: MongoDB dependency spans

Closes [#157](https://github.com/Tharga/MongoDB/issues/157) — *No way to emit MongoDB dependency spans:
`ClusterConfigurator` is assigned internally, with no consumer hook.*

## Goal

Let a consumer see MongoDB calls as **dependency spans** inside a request's distributed trace, so a slow
request can be attributed to a specific database command rather than inferred from aggregate durations.

## Background

`MongoDbClientProvider.GetClient` **assigns** `settings.ClusterConfigurator`, and every type that could
reach it (`IMongoDbClientProvider`, `CommandMonitorService`, `ICommandMonitorService`, `CommandEntry`) is
`internal`. There is no `ConfigureCluster` or `MongoClientSettings` callback in `DatabaseOptions` or
`MonitorOptions`. `CommandMonitorService` subscribes only to `CommandSucceededEvent` / `CommandFailedEvent`
and records name, duration and database into a ring buffer — those are **metrics**, and there is no
`CommandStartedEvent` subscription anywhere, so no span could start even if the hook existed.

Reported by Quilt4Net Server, where seven days of Application Insights `dependencies` contain HTTP and
InProc spans and not one database span, leaving a production timeout investigation running on inference.

## Scope

**In:**

1. **Additive cluster configuration.** Compose the configurator instead of assigning it — any pre-existing
   value first, then the built-in monitor subscriptions, then consumer callbacks in registration order.
2. **`DatabaseOptions.ConfigureCluster(Action<ClusterBuilder>)`** — a general escape hatch, additive,
   following the `AddCollectionInterceptor` idiom already in `DatabaseOptions`.
3. **A first-class `ActivitySource`** named `Tharga.MongoDB`, fed by a new internal subscriber on
   `CommandStartedEvent` / `CommandSucceededEvent` / `CommandFailedEvent`, correlated by `RequestId`.
4. **`MonitorOptions.EnableActivitySource`** (default **true**) and **`MonitorOptions.CaptureCommandText`**
   (default **false**).
5. Docs on both surfaces (`README.md` and `docs/articles/monitoring.md`).

**Out:**

- Metrics (`Meter` / `System.Diagnostics.Metrics`). The ask is traces; the existing monitor already covers
  durations and pool gauges.
- Making `IMongoDbClientProvider` / `ICommandMonitorService` public. The hook removes the reason to want
  them, and widening them is a larger surface commitment than the issue needs.
- Emitting spans for the library's own monitor/index/clean plumbing beyond what the driver raises as
  commands.

## Design decisions

- **`EnableActivitySource` defaults to `true`.** `ActivitySource.StartActivity` returns `null` when no
  listener is registered, and the emitter checks `HasListeners()` before doing any work, so a consumer who
  never calls `AddSource("Tharga.MongoDB")` pays a volatile read and allocates nothing. This differs
  deliberately from `EnableCommandMonitoring`, which defaults to `false` because it buffers entries whether
  or not anything consumes them. The flag exists so a consumer who attaches the community
  `DiagnosticsActivityEventSubscriber` through the new `ConfigureCluster` hook can turn ours off rather
  than emit duplicate spans.
- **`CaptureCommandText` defaults to `false`.** The command document contains query values, i.e. data.
- **Handshake and heartbeat commands are excluded** (`hello`, `isMaster`, `buildInfo`, `ping`,
  `saslStart`, `saslContinue`, `getLastError`, `endSessions`, `authenticate`). They are per-connection
  chatter, not consumer-initiated work, and they would drown the trace.
- **Configurator composition is built as an ordered list of steps**, so ordering can be asserted directly
  in tests rather than by introspecting `ClusterBuilder`'s private aggregator.

## Acceptance criteria

- [ ] A consumer registering `o.ConfigureCluster(...)` still gets command monitoring **and** connection-pool
      monitoring — the regression the issue explicitly warns about.
- [ ] Multiple `ConfigureCluster` callbacks all run, in registration order, after the built-ins.
- [ ] A pre-existing `ClusterConfigurator` on the settings is preserved, not overwritten.
- [ ] With an `ActivityListener` on `Tharga.MongoDB`, a driver command produces exactly one `Client`-kind
      activity carrying `db.system`, `db.name`, `db.operation`, `db.mongodb.collection`, `server.address`
      and `server.port`.
- [ ] A failed command sets `ActivityStatusCode.Error` and an `error.type` tag.
- [ ] The span's parent is the ambient `Activity` at call time, so it nests inside a request trace.
- [ ] Excluded commands produce no activity.
- [ ] `db.statement` is absent by default and present when `CaptureCommandText` is set.
- [ ] With no listener registered the emitter allocates **zero** bytes, asserted by measurement (the idiom
      already used by the interception tests), with a guard test so the assertion cannot pass vacuously.
- [ ] `EnableActivitySource = false` registers no subscriber at all.
- [ ] Full test suite is green apart from the 6 known pre-existing failures recorded in the backlog
      (5 `TransactionsTests` needing a replica set, 1 flaky `DeleteWhenOneIsExpired`).
- [ ] Build is clean at 0 warnings.

## Done condition

All acceptance criteria met, both doc surfaces updated, `MAJOR_MINOR` bumped to `2.17` (additive change,
minor release), issue #157 answered and closed, and the `upstream-requests.md` row for it resolved.

## Baseline (before any change on this branch)

`dotnet test -c Release` on `master` + the dependency update: **741 total, 727 passed, 8 skipped, 6 failed**
— the 6 failures being exactly the pre-existing set above. Build: 0 warnings, 0 errors.
