# Feature: Include queue depth in ExecuteLimiter warning log

## Source
Request from Eplicta.Core (Medium priority, 2026-04-01)

## Goal
Add queue depth to the ExecuteLimiter warning log when the concurrent limit is reached, so operators can gauge severity during incidents.

## Scope
- Update the warning log at the concurrent limit to include `state.GetQueued()` count
- Message format: "The maximum number of {count} concurrent executions for {serverKey} has been reached. {queueCount} operations waiting in queue."

## Acceptance Criteria
- [ ] Warning log includes queue depth when concurrent limit is reached
- [ ] Existing log behavior is otherwise unchanged
- [ ] Tests verify the updated log message
- [ ] Notification sent back to Eplicta.Core requests.md

## Done Condition
The ExecuteLimiter warning log includes queue depth for incident triage.
