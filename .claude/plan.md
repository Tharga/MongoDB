# Plan: Call Information Enhancements

## Steps

- [x] Step 1: Fix CallDialog Elapsed bug — Changed `.Milliseconds` to `.TotalMilliseconds` in CallDialog.razor
- [x] Step 2: Add CallCount tracking to ICallLibrary — Added `GetCallCounts()`, `ResetCalls()`, `CallChanged` event
- [x] Step 3: Show CallCount in CollectionView — Added Calls column, injected ICallLibrary
- [x] Step 4: Add call reset feature — Added `ResetCalls()` to IDatabaseMonitor, Reset button in CallView
- [x] Step 5: Add filters to CallView — Added Collection, Function, Operation filter dropdowns
- [x] Step 6: Real-time ongoing updates via events — Event-driven refresh when Ongoing tab active
- [x] Step 7: ExecuteLimiter queue metrics tracking — IQueueMonitor interface, metrics in ExecuteLimiter, DI registration
- [x] Step 8: QueueView component with real-time graph — RadzenChart with queue depth, executing, and wait time series; Queue tab in CallView

## Last session
All 8 steps completed. Build succeeds, 205 tests pass. Ready for final review and commit.
