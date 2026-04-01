# Plan: Driver Command Monitoring

## Steps

### Step 1: Add config flag and command event tracker
- [x] Add `EnableCommandMonitoring` to `MonitorOptions`
- [x] Create `CommandMonitorService` that tracks driver command durations
- [x] Wire into `MongoDbClientProvider` via `ClusterConfigurator` when enabled
- [x] Build and verify

### Step 2: Surface driver duration in call steps
- [x] In `DiskRepositoryCollectionBase.ExecuteAsync`, after Action step, look up driver duration
- [x] Add as message on the Action step (e.g. "Driver: find 12.34ms")
- [x] Build and verify

### Step 3: Tests, README, and commit
- [x] Write tests (3 tests)
- [x] Update README
- [x] Final build and test (240 passed)
- [ ] Commit all changes
