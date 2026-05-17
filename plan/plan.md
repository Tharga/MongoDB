# Plan: Monitor auth surface

Feature scope: see [feature.md](feature.md). Branch: `feature/monitor-auth-surface`. Closes #96 + Asks #1+#2 of #100.

## Steps

### Phase 1 — API surface ✅

- [x] **1.1** New public `MongoDbMonitorOptions` with `ApiKeys` array and `UseApiKeyValidator<TValidator>()` that captures an internal forwarder over `CommunicationOptions.RegisterApiKeyValidator<T>()`.
- [x] **1.2** New `AddMongoDbMonitorServer(WebApplicationBuilder, Action<MongoDbMonitorOptions>)` overload — wires `ApiKeys` + captured validator forwarder into the underlying `AddThargaCommunicationServer`. Throws `ArgumentNullException` on null configure.
- [x] **1.3** Old `(primaryApiKey, secondaryApiKey)` overload now `[Obsolete]` — folds both strings into the new overload's `ApiKeys`. Sample dogfooded to the new pattern; full repo builds with 0 deprecation warnings.
- [x] **1.4** 7 tests in `MonitorServerRegistrationOptionsTests`: option capture, validator registration in DI, ArgumentNullException on null configure, legacy overload still wires the full pipeline, legacy fold drops null/whitespace and preserves real values.

### Phase 2 — `AuthKeyName` plumbing ✅

- [x] **2.1** `MonitorClientDto` — new `AuthKeyName` field with XML doc.
- [x] **2.2** Bridge updated — `MonitorClientStateService.Build` now copies `KeyId`/`KeyName` from upstream `IClientConnectionInfo`; `MonitorClientBridge.OnConnectionChanged` propagates `KeyName` into the `MonitorClientDto.AuthKeyName` field.
- [x] **2.3** Verified via Tharga.Communication 0.2.0 XML docs — `IClientConnectionInfo.KeyId/KeyName` get populated automatically by the upstream validator pipeline; our Build copy is what makes it visible inside the monitor.
- [x] **2.4** 2 tests added to `RemoteActionDelegationTests`: `IngestClientConnected_PreservesAuthKeyName_ThroughGetMonitorClients` and `GetMonitorClientDetail_SurfacesAuthKeyName_ForKeyedAgent`. Skipped the planned direct `Build` test — `ClientStateServiceBase<T>` initialisation pulls more from DI than a minimal test provider supplies; left a note in the test file explaining the reasoning.

### Phase 3 — Seal badge in `ClientsView` ✅

- [x] **3.1** New 40px-wide column in `ClientsView`'s `RadzenDataGrid` — renders a `RadzenIcon Icon="lock"` with tooltip `@context.AuthKeyName` when the agent is keyed; empty cell otherwise.
- [x] **3.2** `_anyKeyed` tracks whether any current row has `AuthKeyName != null`; bound to `Visible` on the column. Recomputed in `OnClientsChanged` so the column appears the moment the first keyed agent connects.
- [x] **3.3** `MonitorClientDialog` header gets the same lock icon next to the connection-state badge when the agent is keyed — consistent visual signal between row and dialog.

### Phase 4 — Tests + smoke

- [x] **4.1** Full `dotnet test -c Release`: 440 passed, 5 Lockable failures from the known pre-existing flaky cohort (verified by isolation runs in earlier PRs). 10 new tests on this branch (7 options + 2 round-trip + 1 already removed Build test).
- [ ] **4.2** Eplicta smoke — user-driven.

### Phase 5 — Close-out

- [ ] **5.1** Commit milestones (API surface + tests, plumbing + tests, badge UI). Single squashed commit at user direction is also fine — defer the call to commit time.
- [ ] **5.2** Write the follow-up spec `planned/monitor-key-last-used-panel.md` so #100 Ask #3 isn't lost.
- [ ] **5.3** Archive `plan/feature.md` to `done/monitor-auth-surface.md`. Update `planned/README.md`.
- [ ] **5.4** `git rm -r plan`, final commit `feat: monitor-auth-surface complete`, push, open PR with `closes #96` and `refs #100` (deliberately not closing #100 — Ask #3 deferred).

## Last session

Branch created, plan written. No code yet. Starting Phase 1 on user confirmation (already given — "lets go").

## Phase 1 results

_To be filled in._
