# Feature: Delegate collection actions to remote agents

## Source
Follow-up from feature #14 (remote-collection-metadata)

## Goal
When a user performs an action (Touch, Drop Index, Restore Index, Clean) on a collection that the server doesn't have direct access to, delegate the action to a connected agent that does.

## Dependencies
- **Blocked on:** Tharga.Communication request-response support (client-side `SendMessage` is not implemented)

## Scope
- Server sends action request to an agent via `SendMessageAsync<TRequest, TResponse>`
- Agent executes the action on its local database and returns the result
- Server picks the best agent: prefer local (server) access, fall back to any connected agent
- UI shows which agent executed the action

## Acceptance Criteria
- [ ] Tharga.Communication supports client-side request-response
- [ ] Actions on remote-only collections are delegated to a connected agent
- [ ] Actions on locally accessible collections execute locally (no delegation)
- [ ] Result is returned to the UI

## Done Condition
Collection actions work regardless of whether the server has direct database access.
