# Plan: Expose MongoDB Monitor data via API-friendly service

## Steps
- [x] 1. Create serialization-friendly DTOs (MonitorDto.cs)
- [x] 2. Add new methods to `IDatabaseMonitor`
- [x] 3. Implement in `DatabaseMonitor` (with IQueueMonitor injection)
- [x] 4. Implement stubs in `DatabaseNullMonitor`
- [x] 5. Update README with minimal API sample
- [x] 6. All 208 tests pass
- [~] 7. Commit
