# Feature: Expose MongoDB Monitor data via API-friendly service

## Source
Eplicta.Core request (2026-03-27, High priority)

## Goal
Make monitor metrics available programmatically via an injectable service, so consuming apps can expose them through REST API endpoints.

## Scope
- Create `IDatabaseMonitorService` interface
- Expose: recent operations (duration, collection, filter, type), slow query log, ExecuteLimiter state (queue depth, concurrent count per key), explain output, collection statistics
- Reuse existing monitor data structures
- Register service in DI container

## Acceptance Criteria
- [ ] `IDatabaseMonitorService` interface exists with query methods
- [ ] Service is injectable via standard DI
- [ ] All existing monitor data is accessible programmatically
- [ ] Existing Blazor UI is not broken (can optionally use the same service)
- [ ] Tests cover service methods

## Done Condition
Consuming applications can inject the service and wire it to their own API controllers to expose monitor data.
