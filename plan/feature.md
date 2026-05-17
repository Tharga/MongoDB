# Feature: Per-agent detail dialog in `ClientsView`

Closes [#99](https://github.com/Tharga/MongoDB/issues/99) on merge (Ask #3 — the last unimplemented ask of the issue). Asks #1 + #2 shipped in `done/monitor-source-column-fix.md`; Ask #4 confirmed already-working during the earlier smoke.

## Goal

Make the rows in `ClientsView` clickable and open a `MonitorClientDialog` that answers *"is agent X reporting and what is it sending right now?"* — the diagnostic question the issue reporter raised. Today the operator can see *that* an agent is connected; this feature lets them see *what* each agent is contributing.

## Scope

Dialog with four sections, all reading data that already exists in `DatabaseMonitor`:

1. **Identity** — machine, application name, version, connection state, ConnectTime / DisconnectTime, resolved `SourceName`.
2. **Collections received** — count + list of collection keys for which this agent appears in `_collectionSources` (reverse-lookup over the per-key source bag tagged at write time).
3. **Calls received** — count + most recent N (default 20) `CallInfo` records where `SourceName == agent.SourceName`.
4. **Latest queue snapshot** — `_remoteQueueStates[sourceName]` rendered as a small panel (queue depth, executing count, last wait time).

Plus: refresh button, disconnected-agent badge, empty-state placeholders when a section has no data yet.

### API surface decision

Going with **Option B** from the planned spec — a single `IDatabaseMonitor.GetMonitorClientDetail(string sourceName, int recentCallLimit = 20)` returning a `MonitorClientDetail` record. Friendlier to the Blazor consumer (atomic snapshot, one round-trip). The dialog is the only consumer for the foreseeable future, so coupling the API to the dialog's shape is acceptable; we can split into Option A's three methods later if a second consumer needs them.

### NuGet bundle

- `Tharga.Blazor` 2.1.4 → 2.1.6 (directly enables Blazor work; 2.1.5 added `CopyButton.Size`).

Deferred (same reasoning as previous PRs):
- `SharpCompress` 0.48.0 → 1.0.0 — CVE-pinned, needs separate verification PR.
- Sample-project bumps (`Tharga.Console` 3.7.4 → 4.1.1 has breaking changes; `Microsoft.AspNetCore.Components.WebAssembly` 10.0.7 → 10.0.8 only affects samples).

## Out of scope

- **Live push** of queue metrics into the dialog — adds subscription lifecycle complexity for marginal benefit. Refresh button is enough in v1.
- **Cross-agent comparison view** — different feature.
- **Disconnect / kick action** on an agent — admin surface, separate ask.

## Acceptance criteria

- Rows in `ClientsView` are clickable; clicking opens `MonitorClientDialog` with the agent's identity in the title.
- The four sections render with non-empty values for an agent that's been emitting data; "—" or "No data yet" placeholders for sections an agent hasn't contributed to.
- The "Recent calls" grid shows source-filtered calls only (no leakage from other agents or local).
- Refresh button updates the dialog content without closing it.
- Disconnected agents still open; their data shows as the last-known snapshot, with a visual disconnected badge.
- Issue #99 is closed by the PR (via `closes #99` in the description).

## Done condition

- Acceptance criteria met.
- Unit tests pass for the new `IDatabaseMonitor.GetMonitorClientDetail` accessor — local-only, remote-only, and mixed scenarios.
- Full suite green (allowing for the same pre-existing Lockable flakiness observed on bare master).
- PR opened, reviewed, merged. Plan archived to `done/`, planned-queue updated.

## Validation environment

Eplicta provides the realistic test bed for the dialog UX:
- Aggregator running `Monitor.Server` + Blazor admin UI.
- One or more Agents (Web or Wpf) actively emitting calls and forwarding collection info.

Smoke confirms: row click opens the dialog, all four sections populate, refresh re-pulls, disconnected agents still openable.
