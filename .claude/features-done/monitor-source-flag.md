# Feature: Monitor Source Flag

## Originating Branch
develop

## Goal
Tag all monitoring data with a source identifier so the UI can distinguish where data originates from (e.g. "SERVER-01/OrderService").

## Scope
1. Add `SourceName` to `MonitorOptions` with default `{MachineName}/{AssemblyName}`
2. Add `SourceName` property to `CallStartEventArgs`, `CallInfo`, `CallDto`, `CallSummaryDto`, `ErrorSummaryDto`
3. Pass source name from config through the event chain
4. Blazor components display source and allow filtering by it

## Acceptance Criteria
- [ ] All monitoring data includes a source identifier
- [ ] Source is configurable via `o.Monitor.SourceName = "MyService"`
- [ ] Defaults to `{MachineName}/{AssemblyName}` without configuration
- [ ] Blazor components display source and allow filtering
- [ ] Tests cover source tagging

## Done Condition
Monitoring data is tagged with its origin, visible and filterable in the UI.
