# Feature: Subscription-based monitoring — only send when someone is watching

## Source
Performance optimization + pending request in central Requests.md

## Goal
Remote agents should not send live monitoring data (ongoing calls, queue metrics) unless someone is actively viewing the dashboard. Use Tharga.Communication's subscription mechanism to gate live data.

## Design Decisions
- **Always sent (no gating):** Collection metadata, completed calls (Last/Slow) — low volume, needed for cache
- **Gated by subscription:** Queue metric timer, ongoing call forwarding — high volume, only useful when someone is watching
- **Single topic:** `live-monitoring` — no per-client filtering, all agents send when anyone subscribes
- **Tharga.Communication handles counting:** `SubscriptionManager` reference-counts subscribers, `SubscriptionStateChanged` fires on first/last transitions, client sees `HasSubscribers` as boolean

## Scope
- Blazor components (CallView on Ongoing tab, QueueView) call `SubscribeAsync<LiveMonitoringMarker>()` on mount, dispose on unmount
- Server notifies all connected agents via `SubscriptionStateChanged`
- `MonitorForwarder` checks `HasSubscribers<LiveMonitoringMarker>()` before sending queue metrics and ongoing calls
- Queue metric timer runs but skips sending when no subscribers
- Ongoing call events (CallStart without matching CallEnd) only forwarded when subscribers present

## Dependencies
- Tharga.Communication subscription support (SubscriptionStateTracker, SubscriptionStateChangedHandler) — already implemented

## Acceptance Criteria
- [ ] Queue metrics not sent when no one is viewing Queue tab
- [ ] Ongoing calls not sent when no one is viewing Ongoing tab
- [ ] Data starts flowing instantly when a user opens Ongoing or Queue tab
- [ ] Data stops when the last viewer closes those tabs
- [ ] Collection metadata and completed calls always sent
- [ ] No breaking changes for existing setups

## Done Condition
Remote agents produce zero network traffic for live data when no dashboard tab is viewing it. Data flows instantly when someone starts watching.
