# Plan: Remote Queue Metrics

## Steps

### Step 1: Create message type and forward from client
- [ ] Create `MonitorQueueMetricMessage` in Monitor.Client
- [ ] Add periodic queue state forwarding in MonitorForwarder
- [ ] Build and verify

### Step 2: Server-side ingestion and storage
- [ ] Create `MonitorQueueMetricHandler` in Monitor.Server
- [ ] Add `IngestQueueMetric` to IDatabaseMonitor
- [ ] Store per-source queue state in DatabaseMonitor
- [ ] Register handler
- [ ] Build and verify

### Step 3: Update QueueView to show per-source data
- [ ] Add source name to queue data model
- [ ] Show separate lines per source in charts
- [ ] Show combined current queue count
- [ ] Build and verify

### Step 4: Tests, README, and commit
- [ ] Write tests
- [ ] Update README
- [ ] Final build and test
- [ ] Commit all changes
