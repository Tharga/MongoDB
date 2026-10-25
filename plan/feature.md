# Feature: reported issues, October 2026

Branch: `fix/reported-issues-2026-10` (one PR for all parts).

## Goal

Fix the three issues reported 2026-10-21 to 2026-10-25, and restore Cosmos DB 4.2 compatibility, which
releases 2.18.1 and 2.18.2 lost.

## Scope

### 0. Restore the MongoDB.Driver 3.9.0 floor (#158)
- Commit `25d7547` (cascade-2026-09, 2026-09-28) raised `MongoDB.Driver` 3.9.0 -> 3.12.0 past the documented
  hold, so 2.18.1 and 2.18.2 cannot connect to Azure Cosmos DB for MongoDB (RU) on server version 4.2.
- Verified from the driver source (`WireVersion.cs` per release tag): 3.9.0 is the last release with
  `minWireVersion: Server42`; 3.10.0 through 3.13.0 all require `Server44`. No 3.9.x patch exists.
- Put the floor back at 3.9.0 and keep every other package update.

### 1. Obsolete `GetCollection()` deadlocks under a synchronization context (Neurolito request, 2026-10-25)
- `GetCollection()` blocks on `FetchCollectionAsync()`, whose continuations need the blocked context while it
  holds the per-collection fetch semaphore, so the caller and every later caller of that collection hang.
- Run the fetch on the thread pool (`Task.Run`) so it never needs the caller's context, and say in the
  `[Obsolete]` message that the method blocks.

### 2. Connection credentials leak through `server` (GitHub #171)
- `MongoDbService.ToServerName` keeps the userinfo of the connection string; `CollectionAccessEvent` passes the
  full URL. The value reaches the MCP `mongodb://collections` resource, the persisted `_monitor` collection,
  monitor agent forwarding and the Blazor collection view.
- One sanitizer that keeps only the host list, applied where the value enters: where it is built, on agent
  ingest, and on cache load (so already-persisted rows are cleaned on their next write).

### 3. Expected exceptions are logged at Error before the caller can handle them (GitHub #172)
- Overridable `GetExceptionLogLevel(Exception, string operation)` on the collection, default `Error`, used at both
  catch sites (`ExecuteAsync`, `HandleStreamCursorException`); `LogLevel.None` suppresses the log.

## Acceptance criteria
- Build and full test suite pass on `MongoDB.Driver` 3.9.0.
- `GetCollection()` called on a single-threaded synchronization context completes (test with a timeout).
- No `server` value anywhere (MCP, cache, forwarded DTO, access event) contains userinfo; sanitizer covers
  with/without scheme, `mongodb+srv`, multi-host, query string and database path.
- A collection overriding `GetExceptionLogLevel` controls the level of the library's exception log; default unchanged.
- README and `docs/` updated; Requests.md entry, #171, #172 and #158/#160 records handled.

## Done condition
All acceptance criteria met, user has tested from the pushed branch and said it is done.
