# Feature: Optional MongoDB driver command monitoring

## Originating Branch
develop

## Goal
Add toggleable driver-level command monitoring to distinguish slow server execution from thread pool starvation in the "Action" step.

## Scope
1. Add `EnableCommandMonitoring` to `MonitorOptions` (default `false`)
2. When enabled, subscribe to `CommandSucceededEvent`/`CommandFailedEvent` via `ClusterConfigurator`
3. Add driver duration as a sub-step within the Action step
4. Log command info at Debug level

## Acceptance Criteria
- [ ] `EnableCommandMonitoring` flag in MonitorOptions (default false)
- [ ] When enabled, driver command duration appears in step data
- [ ] No performance impact when disabled
- [ ] Tests cover monitoring toggle
- [ ] Notification sent back to Eplicta.Core

## Done Condition
Operators can toggle driver command monitoring on/off for incident investigation.
