# Requests

## Pending

### ExecuteLimiter: per-key limit does not protect shared connection pool
- **From:** Eplicta.Core (`c:\dev\Eplicta\Core`)
- **Date:** 2026-03-27
- **Priority:** High
- **Description:** The `ExecuteLimiter` creates a separate `SemaphoreSlim` per configuration key (e.g. `MongoDB.Aggregator`, `MongoDB.Document`, `MongoDB.Integration`). Each key gets `MaxConcurrent` slots independently. However, all keys share the same MongoDB driver connection pool (`MaxPoolSize` in the connection string). This means the **effective concurrent limit is `MaxConcurrent × number_of_keys`**, which can far exceed the connection pool size. In Eplicta's case: `MaxConcurrent=500 × 3 keys = 1500` potential concurrent operations, but `MaxPoolSize=300`. The limiter was unable to prevent `MongoWaitQueueFullException` because it allowed 5x more operations than the pool could handle. **Suggested fix:** Change `ExecuteLimiterOptions.MaxConcurrent` to `int?` (nullable). Resolution order: (1) If explicitly set by the user, use that value. (2) If null, read `MongoClientSettings.MaxConnectionPoolSize` from the parsed connection string (the driver always has this — either from an explicit `MaxPoolSize=N` in the connection string or the driver's default of 100). (3) Use that value as the **global** concurrent limit across all keys, enforced by a single shared `SemaphoreSlim`. The per-key semaphores can remain as a secondary mechanism for fairness between configuration names. This way the limiter automatically stays in sync with the actual connection pool size without manual configuration.
- **Status:** Done (2026-03-28) — Limiter now keyed by server key (per connection pool). MaxConcurrent auto-detects from MaxConnectionPoolSize, capped at pool size with warning log.

### Expose MongoDB Monitor data via API-friendly service
- **From:** Eplicta.Core (`c:\dev\Eplicta\Core`)
- **Date:** 2026-03-27
- **Priority:** High
- **Description:** The Tharga MongoDB Monitor currently exposes database operation metrics (call counts, slow calls, duration, collection, filter, explain) only through the Blazor UI component. This data needs to be accessible programmatically so it can be exposed via REST API endpoints. The purpose is to enable AI assistants (Claude) to query and analyze database performance — identifying slow queries, N+1 patterns, connection pool pressure, and other issues without needing access to the Blazor UI. **What is needed:** A service interface (e.g. `IDatabaseMonitorService`) that provides methods to query: (1) recent operations with duration, collection, filter, and operation type, (2) slow query log with thresholds, (3) current ExecuteLimiter state (queue depth, concurrent count per key), (4) explain output for specific operations, (5) collection statistics (sizes, index info). This service should be injectable so consuming applications can wire it to their own API controllers. The existing monitor data structures can be reused — this is about exposing them via a service contract rather than only through UI rendering.
- **Status:** Done (2026-03-28) — Added API-friendly methods to IDatabaseMonitor with serializable DTOs. Includes call summary, error summary, explain, slow calls with index info, and connection pool state.

### Lower default log level for MongoDB operation traces
- **From:** Eplicta.Core (`c:\dev\Eplicta\Core`)
- **Date:** 2026-03-27
- **Priority:** High
- **Description:** The `Measured MongoDB.*` trace in `DiskRepositoryCollectionBase` line 182 is hardcoded as `LogInformation`. This logs every single MongoDB operation with full timing details, producing ~57k entries per 2 hours in production — flooding Application Insights and burying real issues. **Fix:** Change the `Measured` trace to `LogDebug` or `LogTrace` so it's silent by default and only visible when explicitly enabled. Also mark `DatabaseOptions.ExecuteInfoLogLevel` as `[Obsolete]` — it only controls one unrelated log line (index assurance) and is redundant with standard .NET log level filtering (`"Tharga.MongoDB": "Warning"` in appsettings). Consumers currently need to add explicit namespace filters to suppress the noise; the library should be quiet by default.
- **Status:** Done (2026-03-27) — Removed ExecuteInfoLogLevel entirely; changed Measured and Assure index logs to LogDebug

### Publish Tharga.MongoDB with IHostApplicationBuilder overload
- **From:** Tharga.Starter (`c:\dev\tharga\Starter`)
- **Date:** 2026-03-24
- **Priority:** High
- **Description:** The `AddMongoDB` extension currently only has an `IServiceCollection` overload in the published NuGet (2.7.8). The local source has an `IHostApplicationBuilder` overload. This is needed so `builder.AddMongoDB()` works consistently. Also, `Tharga.Team.MongoDB 2.0.1` depends on `Tharga.MongoDB >= 2.8.5-pre.20` which is not published as stable — a stable release of Tharga.MongoDB >= 2.8.5 is needed.
- **Status:** Done (2026-03-25) — README updated, version bumped to 2.9, release gate restored to master-only

### Make UseMongoDB async to avoid sync-over-async blocking
- **From:** Eplicta.Core (`c:\dev\Eplicta\Core`)
- **Date:** 2026-04-01
- **Priority:** High
- **Description:** `UseMongoDB` is currently `void` and uses `Task.WaitAll` to synchronously block on firewall and index assurance tasks (lines 314, 329 of `MongoDbRegistrationExtensions.cs`). These were recently changed to `Task.WhenAll` without `await`, which silently discards the result — making firewall and index tasks fire-and-forget. The correct fix is to make the method async: change signature to `public static async Task UseMongoDBAsync(this IHost app, ...)` and `await Task.WhenAll(task)`. Keep the old `UseMongoDB` as an obsolete sync wrapper calling `UseMongoDBAsync(...).GetAwaiter().GetResult()` for backwards compatibility. In the meantime, the current code should revert to `Task.WaitAll` until this is done.
- **Status:** Done (2026-04-01) — Added error handling to index assurance startup task. Kept UseMongoDB sync (standard .NET startup pattern). Both tasks now have proper try/catch with logging. Task.WaitAll used when WaitToComplete=true.

### Optional MongoDB driver command monitoring for diagnostics
- **From:** Eplicta.Core (`c:\dev\Eplicta\Core`)
- **Date:** 2026-04-01
- **Priority:** Low
- **Description:** When investigating slow MongoDB operations, the existing step timings (Queue, FetchCollection, Action, Finalize) don't distinguish between slow server execution and thread pool starvation — both show up as a slow "Action" step. MongoDB driver command monitoring (`CommandStartedEvent`/`CommandSucceededEvent` via `ClusterConfigurator`) would provide the driver-level round-trip time (network + server), separate from thread pool wait. However, this should NOT be always-on in production due to volume and sensitive data in command bodies. **Suggested approach:** Add a `DatabaseOptions.EnableCommandMonitoring` flag (default `false`) that can be toggled via config (`MongoDB:EnableCommandMonitoring`). When enabled, subscribe to `CommandSucceededEvent` and log the command name, collection, and duration at `LogDebug` level. Optionally add the driver duration as a sub-step within the "Action" step in the monitor data. This allows operators to turn it on temporarily during incidents and off again afterwards.
- **Status:** Pending

### Include queue depth in ExecuteLimiter warning log
- **From:** Eplicta.Core (`c:\dev\Eplicta\Core`)
- **Date:** 2026-04-01
- **Priority:** Medium
- **Description:** When the ExecuteLimiter logs "The maximum number of {count} concurrent executions for {serverKey} has been reached", the message does not include the number of operations waiting in the queue. During production incidents this makes it impossible to gauge severity — hitting the limit with 2 items in the queue is very different from hitting it with 500. **Suggested fix:** Add the queue depth to the warning log: `"The maximum number of {count} concurrent executions for {serverKey} has been reached. {queueCount} operations waiting in queue."` The value is already available via `state.GetQueued()` at line 94 of `ExecuteLimiter.cs`.
- **Status:** Done (2026-04-01) — Added queue depth to the concurrent limit warning log.

## Notifications

### Null-safe config binding in AddThargaCommunicationClient — DONE
- **From:** Tharga.Communication (`c:\dev\tharga\Toolkit\Communication`)
- **Completed:** 2026-03-31
- **Summary:** Added null-coalesce to `Get<CommunicationOptions>()` so missing `Tharga:Communication` config section no longer throws NullReferenceException. Options callback alone can now provide all required values.
- **Branch/Version:** feature/null-safe-config (merged to develop)

### API key authentication for SignalR connections — DONE
- **From:** Tharga.Communication (`c:\dev\tharga\Toolkit\Communication`)
- **Completed:** 2026-04-01
- **Summary:** Added API key authentication to SignalR connections. Client sends `X-Api-Key` header when `ApiKey` is configured. Server validates against `PrimaryApiKey`/`SecondaryApiKey` — rejects invalid keys, accepts all when no keys configured (backwards compatible). Supports zero-downtime key rotation via dual keys.
- **Branch/Version:** v1.1.0 (feature/api-key-auth on develop)
