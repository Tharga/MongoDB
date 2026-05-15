# Feature: Monitor source-column visibility fix

References [#99](https://github.com/Tharga/MongoDB/issues/99). Does **not** close it — closing waits for the per-agent dialog follow-up.

## Goal

Make the **Source** column and **Sources** filter actually appear on the Aggregator UI when running `Tharga.MongoDB.Monitor.Server` with one or more remote agents. Today the canonical Aggregator + 1 Agent topology never trips the visibility threshold even when both sides are happily emitting data.

## Scope

Two related bug fixes:

1. **CollectionView source tagging.** `DatabaseMonitor._collectionSources` is only written from the remote ingest path (`IngestCollectionInfo`). Local collections are never tagged with `_mongoDbServiceFactory.SourceName`, so `GetCollectionSources(localKey)` returns empty and the local Aggregator doesn't count toward `_sources.Length > 1`. Fix: tag local collections at the same point they're surfaced to the monitor.

2. **CallView filter refresh.** `CallView.razor`'s `UpdateFilterOptions()` is only invoked from `OnParametersSetAsync`. Calls arriving via `OnCallChanged` notify the grid but don't refresh `_sources`/`_showSource`. If the page was opened before remote calls arrived, the dropdown stays stale until a full page reload. Fix: refresh the filter-option set on incoming-call events.

## Out of scope

- **Ask #3 of issue #99** (clickable agents in `ClientsView` with a per-agent detail dialog) — moves to a separate follow-up feature once this bug fix ships. Tracked in [planned/](../../SynologyDrive — see plans/Toolkit/MongoDB).
- **Ask #4 of issue #99** (drill-down for remote-only collections) — appears already shipped via `GetInstanceAsync` checking `_remoteCollections` first. Will smoke-confirm during this feature; if a repro exists, file a small follow-up.
- Outdated NuGet packages (`Microsoft.Extensions.*`, `MongoDB.Driver`, `Tharga.Runtime`, `SharpCompress`) — deferred to a separate maintenance bump.

## Acceptance criteria

- With an Aggregator and ≥1 Agent both emitting data, both the **Collections** tab and the **Calls** tab show the **Source** column and the **Sources** filter dropdown, with both source names listed.
- The **Sources** filter actually narrows the rows when one or more sources are selected.
- The **Calls** tab source list updates when new remote calls arrive, without requiring a page reload.
- Existing tests stay green; new unit tests cover the two fixes.

## Done condition

- Acceptance criteria above met.
- PR opened and merged, with PR description referencing #99 (not closing).
- No README change required — these are bug fixes, not new consumer-visible features.

## Validation environment

Eplicta provides the realistic multi-client test bed:

- **Aggregator** — `Eplicta.Aggregator` at `c:\dev\Eplicta\Core`, `o.Monitor.SourceName = "Aggregator"`.
- **Agent** — `Eplicta.Agent.Web` / `Eplicta.Agent.Wpf`, default `{MachineName}/{EntryAssembly}` SourceNames.

Smoke-confirm after the unit tests pass.
