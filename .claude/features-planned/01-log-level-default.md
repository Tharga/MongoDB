# Feature: Lower default log level for MongoDB operation traces

## Source
Eplicta.Core request (2026-03-27, High priority)

## Goal
Stop flooding production logs with per-operation trace entries by changing the default log level from `LogInformation` to `LogDebug` or `LogTrace`.

## Scope
- Change the `Measured MongoDB.*` trace in `DiskRepositoryCollectionBase` (line ~182) from `LogInformation` to `LogDebug`
- Mark `DatabaseOptions.ExecuteInfoLogLevel` as `[Obsolete]`
- Ensure standard .NET log level filtering works without workarounds

## Acceptance Criteria
- [ ] `Measured` trace defaults to `LogDebug` or `LogTrace`
- [ ] `ExecuteInfoLogLevel` is marked `[Obsolete]` with a helpful message
- [ ] Existing tests pass
- [ ] No breaking changes to public API

## Done Condition
Default log output is quiet; consumers only see operation traces when they explicitly enable Debug/Trace level for the Tharga.MongoDB namespace.
