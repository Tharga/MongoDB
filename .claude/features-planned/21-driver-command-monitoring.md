# Feature: Optional MongoDB driver command monitoring for diagnostics

## Source
Request from Eplicta.Core (Low priority, 2026-04-01)

## Goal
Add toggleable driver-level command monitoring to distinguish slow server execution from thread pool starvation in the "Action" step.

## Scope
- Add `DatabaseOptions.EnableCommandMonitoring` flag (default `false`)
- When enabled, subscribe to `CommandSucceededEvent` via `ClusterConfigurator`
- Log command name, collection, and duration at `LogDebug` level
- Optionally add driver duration as a sub-step within the "Action" step in monitor data
- Must NOT be always-on in production (volume and sensitive data in command bodies)

## Acceptance Criteria
- [ ] `EnableCommandMonitoring` flag in config (default false)
- [ ] When enabled, driver command events are captured and logged at Debug level
- [ ] Driver duration appears as a sub-step within the Action step
- [ ] No performance impact when disabled
- [ ] Tests verify monitoring toggle
- [ ] Notification sent back to Eplicta.Core requests.md

## Done Condition
Operators can toggle driver command monitoring on/off for incident investigation.
