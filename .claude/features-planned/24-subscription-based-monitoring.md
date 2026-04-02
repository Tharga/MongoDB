# Feature: Subscription-based monitoring — only send when someone is watching

## Source
Performance optimization + pending request in central Requests.md

## Goal
Remote agents should not send monitoring data unless someone is actively viewing the dashboard. Use Tharga.Communication's subscription mechanism to notify clients when to start/stop sending.

## Scope
- Server notifies connected clients "someone is watching" when a Blazor component subscribes
- Server notifies "no one is watching" when all subscribers disconnect
- MonitorForwarder only forwards calls and collection info when watching is active
- Initial collection info is still sent on connect (for the Collections tab)
- Call data and queue metrics are gated by subscription state
- Use SubscriptionStateTracker from Tharga.Communication

## Dependencies
- Tharga.Communication subscription support (SubscriptionStateTracker, SubscriptionStateChangedHandler)

## Acceptance Criteria
- [ ] No call data sent when no one is viewing the dashboard
- [ ] Call data starts flowing when a user opens the Calls or Queue tab
- [ ] Data stops when the last viewer disconnects
- [ ] Collection metadata is always sent (not gated)
- [ ] No breaking changes for existing setups

## Done Condition
Remote agents produce zero network traffic for call/queue data when no dashboard is open. Data flows instantly when someone starts watching.
