# Feature: multi-host connection strings, and the failed-index recheck key

Branch: `fix/failed-index-recheck-server-key`.

## Findings (2026-10-25)

1. **Multi-host connection strings do not work at all.** The driver's `MongoClientSettings.Server` and
   `MongoUrl.Server` getters are `_servers.Single()`, so they throw `InvalidOperationException: Sequence contains
   more than one element` when the connection string lists hosts (`mongodb://h1:27017,h2:27018/db?replicaSet=rs0`).
   `MongoDbService` reads them in `AssureFirewallAccessAsync` (runs inside every `GetCollectionAsync`),
   `GetDatabaseHostName`, `GetDatabaseAddress` and `GetDatabaseDescription`. Verified with a throwaway probe:
   `GetCollectionAsync` throws, so every repository operation fails. Affects self-hosted replica sets and Atlas's
   standard (non-SRV) connection string. SRV, localhost and Cosmos DB use one host, which is why it went unnoticed.
2. **The failed-index recheck never matches on non-default ports or host lists.** `FailedIndexRecheckService` asks
   `DatabaseMonitor.GetCollectionsWithFailedIndices`, which joins the initiation library's failures, keyed by
   `RepositoryCollectionBase.ServerName` (= `GetDatabaseHostName()`, host only), to `CollectionInfo.Server` (host
   list with non-default ports, e.g. `acct.mongo.cosmos.azure.com:10255`). Only a single host on the default port
   matches. Before 2.18.3 any credentialed connection string never matched either.

## Scope

- `MongoDbService`: every single-server read becomes multi-host safe.
  - `AssureFirewallAccessAsync`: skip the firewall only when every host is localhost.
  - `GetDatabaseHostName`: the host names, comma-separated (unchanged for one host).
  - `GetDatabaseAddress`: `host:port` per server, comma-separated (unchanged for one host).
  - `GetDatabaseDescription`: the server display name.
- `RepositoryCollectionBase.ServerName` uses the same server display name as `CollectionInfo.Server`
  (new internal `IMongoDbServiceInternal.ServerDisplayName`), so the recheck join matches.
  Observable change: `ActionEventArgs.ContextData.ServerName` now carries the port when it is not the default,
  and the host list for multi-host strings.

## Acceptance criteria
- A `MongoDbService` on a multi-host URL: `GetCollectionAsync` works; host name and address list every server;
  the firewall is skipped only when every host is local.
- For localhost, Cosmos-style `:10255`, multi-host and SRV URLs, a collection's `ServerName` equals the
  `CollectionInfo.Server` the monitor records for it.
- `GetCollectionsWithFailedIndices` finds a failure recorded by a collection on a Cosmos-style URL.
- Full suite passes; docs updated where the behaviour is described.

## Done condition
User confirms; PR merged.
