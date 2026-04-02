# Feature: Remote queue metrics from clients

## Source
Monitoring gap — server only shows local queue state

## Goal
Display queue information (queue depth, executing count, wait time) from remote agents alongside local queue data, so the dashboard gives a complete picture of all connection pools.

## Scope
- Client periodically sends `QueueMetricEventArgs` to the server (or on-demand via subscription)
- New message type `MonitorQueueMetricMessage` in Monitor.Client
- Server-side handler aggregates local + remote queue metrics
- QueueView Blazor component shows per-source queue state
- `GetConnectionPoolState()` returns combined state or per-source breakdown

## Acceptance Criteria
- [ ] Remote agent queue metrics are forwarded to the server
- [ ] QueueView shows queue state per source (local + remote)
- [ ] `GetConnectionPoolState()` includes remote metrics
- [ ] No overhead when no agents are connected

## Done Condition
Dashboard shows queue depth and executing count from all agents, not just the local server.
