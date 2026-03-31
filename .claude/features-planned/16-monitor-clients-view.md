# Feature: Connected clients view in Blazor dashboard

## Source
Operational visibility for distributed monitoring

## Goal
Add a "Clients" tab to the Blazor database dashboard showing all connected monitoring agents, their status, and connection history.

## Scope
- New `ClientsView.razor` component showing connected/disconnected agents
- Display: machine name, application type, version, connection time, disconnect time, status (connected/disconnected)
- Persist connection history so disconnected clients remain visible (with lost-connection indicator)
- Storage: in-memory by default, with option for database storage
- Storage consideration: the monitor server may have a different database configuration than the monitored collections — evaluate where to store client state (separate `_monitor_clients` collection, or in-memory with optional persistence)
- Place as a new tab after "Calls" in the dashboard

## Acceptance Criteria
- [ ] ClientsView component shows all known agents
- [ ] Connected clients show green/active status
- [ ] Disconnected clients show with disconnect time and lost-connection indicator
- [ ] Client list persists across page navigations (in-memory minimum)
- [ ] Tab appears in the dashboard after Calls

## Done Condition
The Blazor dashboard has a "Clients" tab showing all connected and recently disconnected monitoring agents with their status.
