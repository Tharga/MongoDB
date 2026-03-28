# Feature: Expose MongoDB Monitor data via API-friendly service

## Originating Branch
develop

## Goal
Add serialization-friendly methods to `IDatabaseMonitor` so consuming apps can expose monitor data via REST API for AI-assisted database analysis.

## Scope
New methods on `IDatabaseMonitor`:
1. `GetExplainAsync(Guid callKey)` — resolve explain plan as string
2. `GetCallCounts()` — per-collection call counts
3. `GetCallSummary()` — group calls by collection+function with count/avg/max
4. `GetErrorSummary()` — failed calls grouped by exception type+collection
5. `GetSlowCallsWithoutIndex()` — slow calls where filter fields lack index coverage
6. `GetConnectionPoolState()` — aggregate pool pressure view

All return serialization-friendly DTOs.
README documents how to wire to minimal API.

## Acceptance Criteria
- [ ] New methods added to `IDatabaseMonitor`
- [ ] All return types are JSON-serializable records
- [ ] `DatabaseNullMonitor` implements stubs
- [ ] Tests pass
- [ ] README documents minimal API wiring

## Done Condition
Consuming apps can expose database performance data via REST with minimal code.
