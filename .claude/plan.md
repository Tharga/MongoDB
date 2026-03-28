# Plan: Monitor Source Flag

## Steps

### Step 1: Add SourceName to MonitorOptions and wire through config
- [x] Add `SourceName` property to `MonitorOptions` with default `{MachineName}/{EntryAssemblyName}`
- [x] Pass source name to `MongoDbServiceFactory` so it's available at event fire time
- [x] Build and verify

### Step 2: Add SourceName to event args and CallInfo
- [x] Add `SourceName` to `CallStartEventArgs`
- [x] Add `SourceName` to `CallInfo`
- [x] Set source in `FireCallStartEvent` from factory
- [x] Propagate through `CallLibrary.StartCall`
- [x] Build and verify

### Step 3: Add SourceName to DTOs
- [x] Add `SourceName` to `CallDto`
- [x] Add `SourceName` to `CallSummaryDto` and `ErrorSummaryDto`
- [x] Update `DatabaseMonitor.ToCallDto` and summary methods
- [x] Write tests (6 tests)
- [x] Build and run tests (222 passed)

### Step 4: Update Blazor components
- [x] Add Source column to CallView (auto-visible when multiple sources)
- [x] Add source filter dropdown to CallView
- [x] Build and verify

### Step 5: Update README and commit
- [x] Update README with source identification documentation
- [x] Final build and test
- [ ] Commit all changes
