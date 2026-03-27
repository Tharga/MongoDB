# Feature: Lower default log level for MongoDB operation traces

## Goal
Stop flooding production logs with per-operation trace entries by lowering log levels and removing the unused `ExecuteInfoLogLevel` option.

## Originating Branch
develop

## Scope
- Remove `ExecuteInfoLogLevel` property from `DatabaseOptions`
- Remove `GetExecuteInfoLogLevel()` from interfaces and implementations
- Remove `_executeInfoLogLevel` field from `RepositoryCollectionBase`
- Change "Measured" log from `LogInformation` to `LogDebug`
- Change "Assure index" log from `_executeInfoLogLevel` to `LogDebug`
- Remove registration mapping in `MongoDbRegistrationExtensions`
- Update test mocks

## Acceptance Criteria
- [ ] `Measured` trace defaults to `LogDebug`
- [ ] `ExecuteInfoLogLevel` is fully removed
- [ ] Existing tests pass
- [ ] "Assure index" log uses `LogDebug` directly

## Done Condition
Default log output is quiet; consumers only see operation traces when they explicitly enable Debug/Trace level for the Tharga.MongoDB namespace.
