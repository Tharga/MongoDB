# Plan: NuGet dependency update — May 2026

Feature scope: see [feature.md](feature.md). Branch: `feature/dependencies-update`.

## Steps

All six bumps applied in a single commit per user direction.

### Done

- [x] **1** Sample-project patches: `Microsoft.Extensions.Hosting` 10.0.7 → 10.0.8 in `SimpleConsoleDemo`/`ConsoleSample`; `Microsoft.AspNetCore.Components.WebAssembly[.Server]` 10.0.7 → 10.0.8 in the template Blazor samples.
- [x] **2** `Tharga.Mcp` 0.1.3 → 0.1.4 — clean, no code changes.
- [x] **3** `Tharga.Communication` 0.1.5 → 0.2.0 — required two small adaptations in `Tharga.MongoDB.Monitor.Server`:
  - `IClientConnectionInfo` gained `KeyId` and `KeyName` (the per-key auth visibility upstream). `MonitorClientConnectionInfo` now declares them as no-op defaults; populating real values is GitHub [#100](https://github.com/Tharga/MongoDB/issues/100)'s job.
  - `CommunicationOptions.PrimaryApiKey` / `SecondaryApiKey` were collapsed into a single `ApiKeys: string[]`. `MonitorServerRegistration.AddMongoDbMonitorServer` keeps its `primaryApiKey` / `secondaryApiKey` parameter shape (no consumer migration needed) and now folds them into the new `ApiKeys` array internally.
- [x] **4** `Tharga.Console` 3.7.4 → 4.1.1 — no code changes in `ConsoleSample` (the breaking `ICommand.IsHidden` → `IsVisible` rename simply isn't used).
- [x] **5** `SharpCompress` 0.48.0 → 1.0.0 — CVE-pin replacement. Inline comment in `Tharga.MongoDB.csproj` updated from "until MongoDB.Driver itself ships SharpCompress >= 0.48.0" to ">= 1.0.0". The floor still holds well above GHSA-6c8g-7p36-r338's affected range (≤ 0.47.4).
- [x] **6** Build + tests: full build clean (0 warnings). Targeted monitor + Communication tests: 36/36 green. Full suite: 430 passed, same pre-existing Lockable cohort failures (5 transaction + 1 DeleteMany timing) as bare master — unchanged by these bumps.

### Notable side effect

`Tharga.Communication` 0.2.0 ships the upstream that GitHub #100 was blocked on:

- `IClientConnectionInfo.KeyId` / `KeyName` for surfacing which API key authenticated each connection.
- `IApiKeyValidator` + `ApiKeyValidationResult` for custom validators (and a default `DefaultApiKeyValidator` reading `ApiKeys`).

This unblocks #100, but per feature.md scope, plumbing real `KeyId`/`KeyName` values through `MonitorClientConnectionInfo` → `MonitorClientDto` → the Blazor seal-badge UI is a **separate consumer-side feature**, not part of this PR.

### Phase 5 — Close-out

- [ ] **5.1** Push.
- [ ] **5.2** On user confirmation: archive `plan/feature.md` to `done/dependencies-update-2026-05.md`, `git rm -r plan`, final commit `chore: dependencies-update complete`, push, open PR.

## Last session

Plan written. No bumps yet. Awaiting user confirmation before starting Phase 1.
