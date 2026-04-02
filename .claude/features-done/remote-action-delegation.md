# Feature: Remote Action Delegation

## Originating Branch
develop

## Goal
When a user performs an action (Touch, Drop Index, Restore Index, Clean) on a collection that the server doesn't have direct access to, delegate the action to a connected agent that owns that collection.

## Scope
1. Add `SourceName` to `MonitorClientConnectionInfo` / `MonitorClientDto` for agent identification
2. Create shared action request/response DTOs
3. Add `SendMessageHandlerBase` implementations on the client for each action
4. In `DatabaseMonitor`, check if collection is remote-only and delegate via `IServerCommunication.SendMessageAsync`
5. Server picks the right agent by matching collection source to connected client

## Acceptance Criteria
- [ ] Actions on remote-only collections are delegated to a connected agent
- [ ] Actions on locally accessible collections execute locally (no delegation)
- [ ] Result is returned to the UI
- [ ] Tests pass

## Done Condition
Collection actions work regardless of whether the server has direct database access.
