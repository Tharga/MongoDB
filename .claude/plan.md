# Plan: Subscription-based monitoring

## Steps

### Step 1: Create marker type and gate client forwarding
- [ ] Create `LiveMonitoringMarker` in Monitor.Client (shared topic type)
- [ ] Gate queue metric sending behind `HasSubscribers<LiveMonitoringMarker>()`
- [ ] Gate ongoing call forwarding behind same check
- [ ] Build and verify

### Step 2: Blazor components subscribe/unsubscribe
- [ ] QueueView subscribes on mount, disposes on unmount
- [ ] CallView subscribes when Ongoing tab selected, disposes when leaving
- [ ] Build and verify

### Step 3: Tests, README, and commit
- [ ] Write tests
- [ ] Update README
- [ ] Final build and test
- [ ] Commit all changes
