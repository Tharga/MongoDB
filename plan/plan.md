# Plan: multi-host connection strings, and the failed-index recheck key

## Steps
- [x] 1. Dependencies: only `MongoDB.Driver` 3.13.0 offered, held at 3.9.0 (#158). Nothing to apply.
- [x] 2. Tests first: multi-host `MongoDbService` (fetch works, host name/address lists, firewall skip rule); `ServerName` equals `CollectionInfo.Server` across URL shapes; `GetCollectionsWithFailedIndices` finds a Cosmos-style failure.
  - `MultiHostConnectionTests` (11). Before the fix 8 failed: the multi-host cases with `Sequence contains more than one element`, `ServerName` `acct.mongo.cosmos.azure.com` vs recorded `...:10255`, and the monitor join finding nothing. No `Database` trait: nothing connects (fake hosts, mocked firewall).
- [x] 3. Fix `MongoDbService` single-server reads.
  - Firewall skip when every host is local; `GetDatabaseHostName`/`GetDatabaseAddress` join every server (single host unchanged); `GetDatabaseDescription` uses the display name.
- [x] 4. `ServerName` from the server display name via `IMongoDbServiceInternal.ServerName` (named `ServerName` because a `ServerDisplayName` property hid the static `ServerDisplayName` class inside `MongoDbService`).
  - CI-filtered set 509 passed; full suite 846, 838 passed, 8 skipped.
- [x] 5. Full suite; docs (README / server-compatibility: multi-host strings supported; ActionEvent ServerName note).
  - server-compatibility: new "Connection strings with several hosts" section; README requirements sentence. Version-free on purpose: the #174 release run (built as 2.18.4, without this fix) is still waiting for approval. `ContextData.ServerName` is not documented anywhere, so that change goes in the PR description.
- [ ] 6. Records at close: GitHub issue for the bug (public, text shown first) or a note in the PR.

## Notes
- Probe (not committed) on `mongodb://user:secret@h1.example:27017,h2.example:27018/...`: `GetDatabaseHostName`, `GetDatabaseAddress`, `AssureFirewallAccessAsync`, `GetCollectionAsync` all threw `InvalidOperationException: Sequence contains more than one element`; `GetServerKey` worked.

## Last session
2026-10-25: steps 1-5 done and committed locally. Next: close-out on the user's word (issue text, archive plan, remove `plan/`, push, PR).
