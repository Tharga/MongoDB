# Plan: fix client queue metrics not showing (+ self-verification)

> Self-contained for a fresh session. Read `plan/feature.md` first. Work is on
> branch `feature/remote-collection-reachability` (already has many commits this
> effort). Leave `Tharga.MongoDB.Blazor/CallView.razor`'s `InitialView` edit out
> of commits — it's the user's unrelated work.

## How the live-queue path works (current design)
1. Browser opens **Calls → Queue** → `QueueView.razor.OnInitializedAsync` calls
   `ILiveMonitoringSubscription.SubscribeAsync()` →
   `IServerCommunication.SubscribeAsync<LiveMonitoringMarker>()`.
2. Server is supposed to tell agents "a subscriber exists"; the agent gates
   sending on `_clientCommunication.HasSubscribers<LiveMonitoringMarker>()` in
   `MonitorForwarder.OnQueueMetricTick` (timer every `QueueMetricInterval`).
3. Agent posts `MonitorQueueMetricMessage` → server `MonitorQueueMetricHandler`
   → `DatabaseMonitor.IngestQueueMetric` → `_remotePoolStates`.
4. `QueueView` polls `DatabaseMonitor.GetPerPoolQueueState()` (local + remote) and
   draws a line per `{source}::{serverKey}`, grouped by configuration.

`HasSubscribers` is driven by Tharga.Communication pushing `SubscriptionStateChanged`
to clients on the 0↔1 boundary; the client's built-in `SubscriptionStateChangedHandler`
updates a `SubscriptionStateTracker` that `HasSubscribers` reads.

## What was already tried this session (all committed)
- `MonitorClientBridge` replays active subscriptions to an agent on connect.
- `LiveMonitoringSubscriptionService` explicitly `PostToAll`s
  `SubscriptionStateChanged(true)` on subscribe / `false` on last unsubscribe,
  and logs it per agent (Communication tab shows it outbound).
- Agent reports its pool even when idle (so idle-but-has-pool agents still surface).
- Regression test confirms the client keeps the built-in `SubscriptionStateChangedHandler`.
- Agent-side state logging in `OnQueueMetricTick` (not connected / no subscriber /
  no pool / sending).
- Per-agent **Communication** tab in `MonitorClientDialog` (inbound/outbound log).
- Sample file logging: server → `%TEMP%/tharga-monitor-server.log`,
  console → `%TEMP%/tharga-monitor-console.log` (Tharga.* at Trace).

## Current evidence
- Server side shows `LiveMonitoring hasSubscribers=True` going **out**.
- Console (agent) showed **no** queue activity and nothing sent back on `sample
  list`; agent-side logging appeared absent (may need a clean run with the new
  file logging).
- So the break is on the **agent side**: it isn't acting on the subscription
  signal. Open question: does the agent **receive** `SubscriptionStateChanged`
  and does `HasSubscribers<LiveMonitoringMarker>()` flip true?

---

## Phase 1 — Make the path self-verifiable
Goal: reproduce the flow without a human + browser.

### 1a. Integration test with real Tharga.Communication (preferred)
- [x] Investigated Tharga.Communication 0.2.1 public API. **Verdict: in-process
      `TestServer` injection is NOT possible.** The client `CommunicationOptions`
      exposes only `ServerAddress`, `Pattern`, `ReconnectDelays`, `ApiKey`,
      `ClientType`, `ClientMachine`, `SendMessageTimeout`, `AdditionalAssemblies` —
      there is **no** `HttpMessageHandlerFactory`/`HubConnection` hook. The
      `ISignalRHostedService` builds the SignalR connection internally against
      `ServerAddress`. → Use the loopback-Kestrel fallback below.
      Topic/key matching looks correct on inspection: broadcast uses
      `Topic = typeof(LiveMonitoringMarker).FullName, Key = null`; client checks
      `HasSubscribers<LiveMonitoringMarker>(key:null)` → both type-level. But
      `MonitorClientBridge.ReplaySubscriptionsAsync` re-broadcasts the **topic
      string returned by `GetSubscriptions()`** (XML: keys are `"TypeName"` or
      `"TypeName:key"`) — if `SubscribeAsync<T>` registers a key that isn't
      `FullName`, the replay topic won't match the client's check. Runtime test
      needed to confirm.
- [x] Loopback-Kestrel integration test built and **green** (stable across 3 runs,
      ~0.6–0.9s each): `Tharga.MongoDB.Tests/LiveMonitoringIntegrationTests.cs`.
      Hosts the server on `http://127.0.0.1:0`, connects a real client, then:
      asserts agent connects → `HasSubscribers<LiveMonitoringMarker>()` is false →
      server `SubscribeAsync()` → agent's `HasSubscribers` flips **true** → agent
      forwards a `MonitorQueueMetricMessage` (synthetic pool via fake `IQueueMonitor`)
      → server **ingests** it (recording `IDatabaseMonitor`) → dispose → `HasSubscribers`
      flips **false**. Server/client MongoDB deps are fakes (real `DatabaseMonitor`
      is internal + 11 deps); the Communication path is fully real.
- [x] Test is deterministic — bounded `WaitUntilAsync` polling (15s ceiling), no
      fixed sleeps. Tagged `[Trait("Category","Integration")]`.

### 1b. MCP diagnostic tools (chosen as the in-app verification path)  → DONE
Added to `Tharga.MongoDB.Mcp/MongoDbToolProvider.cs` (all `DataAccessLevel.Metadata`,
so listed/callable at the default access level):
- [x] `mongodb.get_monitor_clients` — connected agents + their forwarding config.
- [x] `mongodb.get_per_pool_queue_state` — per-pool queue state across server + all
      agents, plus active subscriptions. The key tool to confirm live metrics flow.
- [x] `mongodb.get_client_communication(sourceName)` — per-agent inbound/outbound log.
- [x] `mongodb.hold_live_subscription(seconds=5,max60)` — opens a real
      `ILiveMonitoringSubscription` for N seconds (resolved optionally via
      `IServiceProvider`; errors cleanly if the monitor server isn't installed), then
      snapshots per-pool state + clients. Drives the live flow headlessly — no browser.
- [x] 7 unit tests added in `McpProviderTests.cs` (count theory updated to 10/15/16);
      full non-Database suite 346 pass / 1 known-flaky fail. Web sample already wires
      `AddMongoDbMonitorServer` + `mcp.AddMongoDB()`, so the tools work there.

### 1c. Sample headless trigger (fallback only)  → NOT NEEDED
- [x] Skipped — 1a (test) + 1b (MCP tools) cover headless drive+observe. No temp
      endpoint or console `--script` mode was created.

## Phase 2 — Pinpoint the break  → RESOLVED: mechanism is sound
The integration test + code inspection settle all four hypotheses:
- [x] (a) **Ruled out** — the test shows the agent *does* receive the signal: its
      `HasSubscribers<LiveMonitoringMarker>()` flips true after the server subscribes.
- [x] (b) **Ruled out** — `HasSubscribers` flips true on subscribe and false on
      dispose; topic/key match works end-to-end.
- [x] (c) Not a code bug — `ExecuteLimiter._states` is `GetOrAdd`-populated on the
      execute path and **never removed**, so once an agent runs *any* DB call its
      pool persists and `GetPerPoolState()` keeps returning it (zeroed when idle).
      The only "no pool" case is an agent that has **never** touched MongoDB — a
      precondition, not a defect.
- [x] (d) **Ruled out by inspection** — `DatabaseMonitor.IngestQueueMetric(source,
      pools)` stores into `_remotePoolStates[source]` and `GetPerPoolQueueState()`
      emits one `{source}::{serverKey}` entry per remote pool; nothing filters it out.

**Conclusion:** the live-queue path works. The earlier session's fixes (explicit
`PostToAll` of `SubscriptionStateChanged` in `LiveMonitoringSubscriptionService` +
`MonitorClientBridge` connect-replay) are the fix, and are now **proven** by an
automated test. The original symptom was most likely (1) pre-fix framework auto-
broadcast not reaching agents (now superseded by the explicit broadcast), and/or
(2) an idle agent that had not yet accessed MongoDB (no pool → nothing to send).

## Phase 3 — Fix  → already in place, now verified
- [x] The explicit-signal approach (hypothesis (a)'s remedy) was applied earlier
      this session and is what the green test exercises. No further code fix needed
      for the mechanism. Open question for cleanup (Phase 4): is the explicit
      broadcast strictly necessary or redundant with the framework's native
      propagation? It's idempotent + harmless, so keep it (belt-and-suspenders)
      unless we add a test that isolates native-only behavior.

## Phase 4 — Verify & finish
- [x] Phase-1 integration test passes (green, stable ×3) and asserts the end-to-end flow.
- [x] Full suite run `--filter "Category!=Database"`: **339 passed, 1 failed** — the
      one failure is the known-flaky `RevalidationQueueTests.HighPriorityKeys_DrainBeforeLow`
      (unrelated). No regression from the new test.
- [ ] **Manual verify (still needed — can't be done headlessly):** sample server +
      console, open Calls → Queue, confirm the console's queue line appears and
      updates on `dynamic add a` / `sample list`. The new file logging
      (`%TEMP%/tharga-monitor-console.log`) will show the agent-side
      `LogLiveStateChange` line ("subscription active — sending queue metrics") to
      confirm in-app. NOTE: the agent must access MongoDB at least once first, else
      it has no pool and (correctly) sends nothing.
- [ ] Decide what diagnostics to keep (Communication tab + file logging are useful;
      agent-side state logging can stay at Debug). No temp 1c endpoint was created.
- [ ] Commit (single-line messages). Likely `test:` for the integration test.

## Housekeeping / gotchas
- Single-line git commit messages; no backticks/`$()`/quoted `;`/newlines in Bash
  (permission analyzer prompts on those). See memory `avoid-permission-prompt-metachars`.
- `.claude/settings*.json` edits may not hot-reload mid-session — reload (`/hooks`)
  or restart for new allow rules to take effect.
- Mongo is local at `mongodb://localhost:27017`; sample server https `:7205` (+ http `:5205`).

## Manual test FAILED (real Blazor app) — reopened
User ran the real stack (server https://localhost:7205 + ConsoleSample agent) and opened
Calls → Queue. Logs (`%TEMP%/tharga-monitor-{server,console}.log`) show:
- Agent connects, sends status/collection info (agent→server works).
- Server registers the subscription (visible under Clients as
  `Tharga.MongoDB.Monitor.Client.LiveMonitoringMarker`).
- **Agent's `HasSubscribers<LiveMonitoringMarker>()` never flips true** — the live-state
  log stays at "no server subscriber" for the whole session; no metrics sent.
- So the **server→agent subscription notification isn't landing** in the real
  deployment — the exact path the isolated test passes. (a)/(b) are back in play for
  the real app; the loopback test couldn't distinguish working delivery.
- `LogComm` only writes the broadcast to the in-memory Communication log, **not** the
  file log — so the file couldn't show whether the server actually broadcast.

### Diagnostic logging added (this turn) — awaiting user re-run
- `LiveMonitoringSubscriptionService`: ILogger added. Logs SubscribeAsync (server-side
  subscriber count), BroadcastAsync (connected-agent count + sources, PostToAll
  completion), unsubscribe, and **surfaces the previously-swallowed PostToAll exception**
  as a Warning.
- `MonitorClientBridge.ReplaySubscriptionsAsync`: logs replayed topics on agent connect
  and surfaces its previously-swallowed exception.
- `MonitorForwarder.OnQueueMetricTick`: per-tick Trace of `connected` + `hasSubscribers`
  so the agent log shows exactly what it observes each second (catches a rapid flip).
- All at Information/Trace under `Tharga.*`, which both sample file logs capture.
- Suite: 347 pass / 0 fail. Next: user re-runs, then read both logs to pinpoint whether
  (1) the server broadcasts, (2) PostToAll throws, (3) the agent receives but doesn't
  flip. Then apply the precise fix (likely Phase 3(a): explicit agent-owned signal).

## Root cause CONFIRMED + fix applied (this turn)
Second manual run (13:00, logs read) was decisive:
- **Server** logged: `SubscribeAsync … 1 subscriber`, `Broadcasting SubscriptionStateChanged(HasSubscribers=True) … Connected agents: 1 [PLUTO/ConsoleSample]`, `PostToAll … completed` (no exception). **The server broadcasts fine.**
- **Agent** logged every tick (incl. all after the broadcast) as `connected=True, hasSubscribers<LiveMonitoringMarker>=False` for 2+ minutes. **The agent's HasSubscribers never flips.**
- ⇒ The framework's `SubscriptionStateChanged → client tracker → HasSubscribers` chain is the broken link in the real deployment (works in the loopback test, not here). Confirmed (a)/(b).

### Fix: explicit, server-owned signal (Phase 3(a)) — DONE, awaiting user re-run
- New `SetLiveMonitoringActiveMessage { bool Active }` (Monitor.Client).
- New `SetLiveMonitoringActiveHandler` (Monitor.Client): logs every receipt ("Received
  SetLiveMonitoringActive(Active=…)") and sets the new internal singleton `LiveMonitoringState`.
- `MonitorForwarder.OnQueueMetricTick` now gates on `LiveMonitoringState.Active ||
  HasSubscribers<LiveMonitoringMarker>()` (explicit flag primary, framework tracker fallback).
- Server `LiveMonitoringSubscriptionService.BroadcastAsync` now `PostToAll`s
  `SetLiveMonitoringActiveMessage` (then the legacy `SubscriptionStateChanged` for back-compat).
- `MonitorClientBridge` connect-replay also sends `SetLiveMonitoringActive(true)` for the live topic.
- 5 new unit tests (`LiveMonitoringSignalTests`); suite 352 pass / 0 fail.
- The handler's receipt log makes the next run conclusive: if the agent logs "Received
  SetLiveMonitoringActive(Active=True)" → server→agent push works and the fix is effective; if not
  → server→agent push is broken at the transport level (bigger issue) and we pivot there.

**VERIFIED WORKING (13:16 run).** Server log shows: `PostToAll SetLiveMonitoringActive(Active=True)
… completed`, then the agent forwards `MonitorQueueMetricMessage` every second with its real pool
(`localhost:27017|pool=100`), including `ExecutingCount=1, WaitTimeMs=9.2` during `sample list`.
Closing the Queue view → `Active=False` → forwarding stops; reopening → `Active=True` again. The
Queue view shows the console's per-pool line. **Bug fixed.**

### Log-level tuning (post-fix, this turn)
- Removed the per-tick "Queue metric tick" Trace (was a diagnostic; on-change `LogLiveStateChange`
  remains the operator signal).
- Server `LiveMonitoringSubscriptionService` + `MonitorClientBridge` subscribe/broadcast/replay logs:
  Information → **Debug** (low-frequency operational events; failure path stays Warning).
- Agent `SetLiveMonitoringActiveHandler` receipt log: Information → **Debug**.
- Net: at Information a consumer sees only the on-change "live monitoring active/stopped" line; Debug
  shows the operational events; Trace is reserved for framework per-message detail.
- NOTE: the per-second `SignalRHub: PostMessageAsync response from agent` flood in the sample logs is
  **framework** (Tharga.Communication) Trace, surfacing only because the sample file loggers set
  `Tharga` = Trace. Tune the sample filter (e.g. `Tharga.Communication` = Debug) if quieter sample
  logs are wanted — not a library concern.

## Last session
Built the Phase-1 self-verification test (`LiveMonitoringIntegrationTests.cs`) over a
real loopback Kestrel + real SignalR client. It is **green and stable**. This settled
Phase 2: the live-queue mechanism is sound — (a)/(b) ruled out by the test, (d) by
inspection, (c) is a precondition (agent must have touched MongoDB once). Phase 3's
fix (explicit `SubscriptionStateChanged` broadcast + bridge replay) was already in
place from earlier and is now proven. Full non-Database suite: 339 pass, 1 known-flaky
fail (unrelated). 1b (MCP diagnostic tools) and 1c (headless trigger) were NOT needed
and can be dropped unless wanted as an ops win.

**Next step:** commit the integration test (`test:` milestone), then the only thing
between here and done is the **manual in-app verify** (Phase 4) — which needs a human
to run the sample server + console and open Calls → Queue (remember: do one DB access
on the agent first). Decide whether to also keep 1b MCP tools as a bonus.

Open decision for the user: is the bug considered fixed (mechanism proven) pending the
manual check, or do you want the extra belt — e.g. an MCP `hold_live_subscription`
tool (1b) so the live flow can be driven/observed headlessly in the real app too?
