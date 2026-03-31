# Feature: Connected clients view in Blazor dashboard

## Source
Operational visibility for distributed monitoring (feature #16)

## Originating Branch
develop

## Goal
Add a "Clients" tab to the Blazor database dashboard showing all connected monitoring agents, their status, and connection history.

## Scope
- Add `MonitorClientDto` to Tharga.MongoDB (DTO for client info)
- Add `GetMonitorClients()` and client changed event to `IDatabaseMonitor`
- Monitor.Server bridges `MonitorClientStateService` events into `IDatabaseMonitor`
- New `ClientsView.razor` component in Tharga.MongoDB.Blazor
- Display: machine name, app type, version, connection time, disconnect time, status
- Disconnected clients remain visible with lost-connection indicator
- Tab placed after "Calls" in the dashboard

## Acceptance Criteria
- [ ] ClientsView shows connected agents with green/active indicator
- [ ] Disconnected agents show with disconnect time
- [ ] Component auto-refreshes on connect/disconnect events
- [ ] Tab appears in sample dashboard
- [ ] No dependency from Blazor project to Monitor.Server

## Done Condition
The Blazor dashboard has a "Clients" tab showing all connected and recently disconnected monitoring agents.
