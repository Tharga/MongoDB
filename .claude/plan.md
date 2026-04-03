# Plan: Full Remote Action Support

## Steps

### Step 1: Fix local DB access for remote collections
- [ ] Fix `GetInstanceAsync` to return remote data without local DB access
- [ ] Fix `RefreshStatsAsync` to skip for remote collections
- [ ] Fix `GetIndexBlockersAsync` to delegate remotely
- [ ] Build and verify

### Step 2: Add remote Find Duplicates
- [ ] Create `GetIndexBlockersRequest`/`GetIndexBlockersResponse` DTOs
- [ ] Add client-side handler
- [ ] Add server-side delegation in DatabaseMonitor
- [ ] Build and verify

### Step 3: Add remote Explain
- [ ] Create `ExplainRequest`/`ExplainResponse` DTOs
- [ ] Add client-side handler (uses local CallLibrary to get ExplainProvider)
- [ ] Update `GetExplainAsync` in DatabaseMonitor to delegate for remote calls
- [ ] Build and verify

### Step 4: Add remote Reset/Clear (broadcast to all)
- [ ] Create `ResetCacheRequest`/`ClearCallHistoryRequest` DTOs
- [ ] Add client-side handlers
- [ ] Update `IRemoteActionDispatcher` with broadcast methods
- [ ] Wire into DatabaseMonitor Reset/Clear methods
- [ ] Build and verify

### Step 5: Tests, README, and commit
- [ ] Write tests
- [ ] Update README
- [ ] Final build and test
- [ ] Commit all changes
