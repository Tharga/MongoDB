# Feature: Make UseMongoDB async to avoid sync-over-async blocking

## Source
Request from Eplicta.Core (High priority, 2026-04-01)

## Goal
Fix `UseMongoDB` which currently uses `Task.WhenAll` without `await`, silently discarding the result of firewall and index assurance tasks.

## Scope
- Add `UseMongoDBAsync` as the primary async method with proper `await Task.WhenAll(task)`
- Mark the old `UseMongoDB` as `[Obsolete]` with a sync wrapper calling `UseMongoDBAsync(...).GetAwaiter().GetResult()`
- Revert the current `Task.WhenAll` (without await) back to `Task.WaitAll` in the obsolete wrapper for safety

## Acceptance Criteria
- [ ] `UseMongoDBAsync` properly awaits firewall and index tasks
- [ ] Old `UseMongoDB` is marked obsolete and delegates to the async version
- [ ] No fire-and-forget of tasks
- [ ] Tests verify async completion
- [ ] Notification sent back to Eplicta.Core requests.md

## Done Condition
`UseMongoDB` pipeline tasks are properly awaited. Old API still works but is marked obsolete.
