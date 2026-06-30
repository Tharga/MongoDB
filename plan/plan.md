# Plan: Monitor client & server library versions in the Blazor UI

See `plan/feature.md` for scope/acceptance. Branch `feature/monitor-library-versions` from `master`.

## Steps
- [x] 0. Branch from master; plan/ files.
- [~] 1. NuGet bumps up front (test/sample only, same as the other branch): JetBrains.Annotations 2026.2.0, Microsoft.NET.Test.Sdk 18.7.0, Swashbuckle.AspNetCore 10.2.3.
- [ ] 2. Core: `AssemblyVersionExtensions.GetLibraryVersion(this Assembly)` (strip `+sha`, fall back to AssemblyName.Version).
- [ ] 3. Core: add `LibraryVersion` to `MonitorClientStatus`; add public `IMonitorServerInfo { string LibraryVersion }`.
- [ ] 4. Client: `LibraryVersion` on `MonitorClientStatusMessage`; set in `MonitorForwarder.SendClientStatusAsync`.
- [ ] 5. Server: map `LibraryVersion` in the status handler; implement + register `IMonitorServerInfo` in `AddMongoDbMonitorServer`.
- [ ] 6. Blazor: ClientsView "Library" column + server-version caption; MonitorClientDialog client library field.
- [ ] 7. Tests: helper parsing; status round-trip carrying LibraryVersion; IMonitorServerInfo returns non-empty.
- [ ] 8. Version bump (build.yml MAJOR_MINOR → 2.13, pending merge order) + docs (monitoring.md, README).
- [ ] 9. Build, full test, commit, push.

## Last session
All steps implemented. Core helper + `IMonitorServerInfo` + `MonitorClientStatus.LibraryVersion` added; client reports its Monitor.Client version on connect; server maps it and registers `IMonitorServerInfo` (Monitor.Server version); Blazor shows a "Library" column, a client-dialog "Library" field, and a "Monitor server vX" caption. Version → 2.13; docs updated (monitoring.md + README). Build clean; full suite 627 passed, 7 environmental replica-set/timing failures (no regressions); 4 new tests green.

**Open before close-out:** push branch for user testing; on confirmation archive feature.md to Plan dir `done/`, `git rm -r plan`, final `feat: … complete` commit, open PR to master. Note: persist-cache branch is 2.12, this is 2.13 — reconcile MAJOR_MINOR at merge time if needed.
