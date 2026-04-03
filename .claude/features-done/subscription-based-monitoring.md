# Feature: Subscription-based monitoring

## Originating Branch
develop

## Goal
Gate live monitoring data (queue metrics, ongoing calls) behind subscriptions so agents don't send data when nobody is watching.

## Scope
1. Create `LiveMonitoringMarker` type as subscription topic
2. Blazor components subscribe on mount, dispose on unmount
3. MonitorForwarder gates queue metrics and ongoing calls behind HasSubscribers
4. Collection metadata and completed calls always sent

## Acceptance Criteria
- [ ] Queue metrics not sent when no one watching
- [ ] Data flows when someone opens Queue/Ongoing tab
- [ ] Data stops when last viewer closes
- [ ] Collection metadata and completed calls always sent
- [ ] Tests pass

## Done Condition
Zero live-data traffic when no dashboard tab is viewing it.
