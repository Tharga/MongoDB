# Plan: Lower default log level for MongoDB operation traces

## Steps
- [x] 1. Remove `ExecuteInfoLogLevel` and change log levels — removed property, interfaces, implementations, field, test mocks, unused imports. Changed "Measured" and "Assure index" logs to `LogDebug`.
- [x] 2. Run tests and verify — 205 passed, 0 failed
- [~] 3. Commit
