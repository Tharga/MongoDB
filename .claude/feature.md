# Feature: Call Information Enhancements

## Goal
Improve call tracking: add per-collection call counts, filters on CallView, real-time ongoing updates, ExecuteLimiter queue graph, and fix the CallDialog Elapsed bug.

## Scope
- CallCount per collection in ICallLibrary (in-memory, per session)
- CallCount column in CollectionView
- Reset calls feature (clears call data and counts)
- CallView filters: Database, Collection, Function, Operation
- Real-time ongoing view via events
- ExecuteLimiter queue graph (queue depth + avg wait time)
- Fix CallDialog Elapsed bug (Milliseconds → TotalMilliseconds)

## Acceptance Criteria
- [x] CallDialog Elapsed shows correct total milliseconds
- [x] CallCount tracked in ICallLibrary, visible in CollectionView
- [x] Reset button in CallView clears all call data including counts
- [x] CallView has filters for Database, Collection, Function, Operation
- [x] Ongoing view updates in real-time via events
- [x] Queue tab in CallView shows live graph of queue depth and wait times
- [x] All existing tests pass
- [x] Solution builds without errors

## Done Condition
All acceptance criteria met, tests green.
