# Feature: Full remote action support for dashboard

## Source
UI gap — many actions and dialogs are disabled or missing for remote collections/calls

## Goal
All actions available in the Blazor dashboard should work for remote agents, not just local data. Actions are delegated to the connected agent via the command pattern in Tharga.Communication.

## Bugs to fix
- **Collection detail dialog tries to read local DB for remote collections** — When opening the detail dialog for a collection from a remote agent, `GetInstanceAsync` / `RefreshStatsAsync` falls through to local MongoDB access using the remote collection's configuration name (e.g. "Default"), which doesn't exist locally. For remote-only collections (`CollectionType == null`), the code must return cached remote data and never attempt local DB access.

## Scope

### Collection detail dialog (remote collections)
- Show the detail button for remote collections (currently hidden via `IsLocal`) — DONE
- Detail dialog displays cached stats, indexes, and clean info from remote data
- All actions delegate to the connected agent:
  - **Touch** — already implemented via remote delegation
  - **Drop Index** — already implemented via remote delegation
  - **Restore Index** — already implemented via remote delegation
  - **Clean** — already implemented via remote delegation
  - **Find Duplicates** (GetIndexBlockersAsync) — NEW: needs remote request/response
- Handle case where no agent is connected (disable actions, show message)
- Refresh data after remote action completes

### Call detail dialog (remote calls)
- **Filter JSON** — already works (sent via CallDto.FilterJson)
- **Explain** — NEW: needs remote request/response. ExplainProvider is a local Func that cannot be serialized. The server must request the explain from the agent that produced the call.

### Global actions (remote)
- **Reset Collection Cache** — NEW: delegate ResetAsync to all connected agents
- **Clear Call History** — NEW: delegate ResetCalls to all connected agents

## Multi-client routing strategy
Multiple agents may have access to the same collection. The routing strategy depends on the action type:

| Action type | Routing | Reason |
|---|---|---|
| Touch, Drop Index, Restore Index, Clean, Find Duplicates | **One** (any connected agent) | Database actions — agents share the same DB |
| Explain | **Specific** (the agent that produced the call) | Only that agent has the ExplainProvider for its own calls |
| Reset Collection Cache, Clear Call History | **All** connected agents | Each agent has its own local cache/history |

## Dependencies
- Remote action delegation (feature #18) — DONE
- Tharga.Communication SendMessage request/response — DONE

## Acceptance Criteria
- [ ] Collection detail dialog opens for remote collections without local DB errors
- [ ] Touch, Drop Index, Restore Index, Clean work on remote collections
- [ ] Find Duplicates works on remote collections
- [ ] Explain works on remote calls (routed to originating agent)
- [ ] Filter JSON displayed for remote calls (already works)
- [ ] Reset Collection Cache delegates to all connected agents
- [ ] Clear Call History delegates to all connected agents
- [ ] Actions disabled with message when no agent is connected
- [ ] Data refreshes after action completes

## Done Condition
All dashboard actions work identically for local and remote data.
