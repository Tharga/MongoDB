# Feature: MCP action parity with Blazor components

## Source
Follow-up from MCP Phase 2 (Tharga.MongoDB.Mcp) — the first release shipped 2 tools (touch, rebuild_index). Blazor components expose more collection-level actions that AI agents should also be able to trigger via MCP.

## Goal
Add MCP tools for the remaining collection and call-level actions so an AI agent can do everything the Blazor admin UI can do — except streaming (Ongoing calls, Queue live gauge) and interactive multi-step dialogs.

## Scope

### New tools
| Tool | Maps to | Args |
|---|---|---|
| `mongodb.drop_index` | `IDatabaseMonitor.DropIndexAsync` | configurationName?, databaseName, collectionName |
| `mongodb.clean` | `IDatabaseMonitor.CleanAsync` | configurationName?, databaseName, collectionName, cleanGuids |
| `mongodb.find_duplicates` | `IDatabaseMonitor.GetIndexBlockersAsync` | configurationName?, databaseName, collectionName, indexName |
| `mongodb.explain` | `IDatabaseMonitor.GetExplainAsync` | callKey |
| `mongodb.reset_cache` | `IDatabaseMonitor.ResetAsync` | (none) |
| `mongodb.clear_call_history` | `IDatabaseMonitor.ResetCalls` | (none) |

### New resource (optional)
- `mongodb://calls/{callKey}` — per-call detail with filter JSON and steps

## Out of scope (explicitly deferred)
- Live streaming (Ongoing calls, Queue live metrics) — MCP `resources/subscribe` support and polling semantics are better suited to non-MCP channels
- Interactive multi-step dialogs (confirm clean, view duplicates inline before deciding to drop) — MCP tools return once; multi-step UX is an agent-scripting concern

## Acceptance Criteria
- [ ] All 6 new tools implemented in `MongoDbToolProvider`
- [ ] All tools delegate to `IDatabaseMonitor` (so remote collections route through existing `IRemoteActionDispatcher`)
- [ ] Error responses (`IsError = true`) when a collection or call is missing, or when no agent is connected for a remote collection
- [ ] Tests cover each new tool
- [ ] README MCP section updated

## Done Condition
An AI agent can inspect and manage MongoDB collections end-to-end via MCP with the same capabilities as the Blazor admin UI (excluding live streaming).
