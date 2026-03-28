# Feature: Add source identification to monitoring data

## Source
Backlog (Medium priority) — prerequisite for distributed monitoring

## Goal
Tag all monitoring data (calls, collections, events) with a source identifier so the UI can distinguish where data originates from — e.g. "Local", "Agent-1", "OrderService".

## Scope
- Add a `Source` property to `CallStartEventArgs`, `CallEndEventArgs`, and `CallDto`
- Default source name derived from application name or configurable via `DatabaseOptions`
- `CollectionInfo` includes source origin
- Blazor components can filter/group by source
- No breaking changes — source defaults to a sensible value when not configured

## Acceptance Criteria
- [ ] All monitoring data includes a source identifier
- [ ] Source is configurable via `AddMongoDB(o => o.Monitor.SourceName = "MyService")`
- [ ] Blazor components display source and allow filtering by it
- [ ] Defaults work without configuration (e.g. uses `{MachineName}/{AssemblyName}`)
- [ ] Tests cover source tagging

## Done Condition
Monitoring data is tagged with its origin, visible in the UI, and ready to support aggregation from multiple sources.
