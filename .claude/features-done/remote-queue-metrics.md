# Feature: Remote Queue Metrics

## Originating Branch
develop

## Goal
Display queue information from remote agents alongside local queue data on the dashboard.

## Scope
1. Create `MonitorQueueMetricMessage` in Monitor.Client
2. Forward queue state snapshots from MonitorForwarder periodically
3. Server-side handler stores per-source queue state in DatabaseMonitor
4. QueueView shows per-source queue metrics (separate lines per source)
5. GetConnectionPoolState includes remote data

## Acceptance Criteria
- [ ] Remote agent queue metrics forwarded to server
- [ ] QueueView shows per-source queue state
- [ ] No overhead when no agents connected
- [ ] Tests pass

## Done Condition
Dashboard shows queue depth and executing count from all agents.
