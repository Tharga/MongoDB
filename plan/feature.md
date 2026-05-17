# Feature: NuGet dependency update — May 2026

Maintenance pass between user-facing features. Brings everything to current except where a pin exists for an explicit reason.

## Scope

Six bumps across the repo, in increasing-risk order:

1. **Sample-project patches** — `Microsoft.Extensions.Hosting` 10.0.7 → 10.0.8 (`SimpleConsoleDemo`, `ConsoleSample`), `Microsoft.AspNetCore.Components.WebAssembly[.Server]` 10.0.7 → 10.0.8 (`Tharga.TemplateBlazor.Web[.Client]`).
2. **`Tharga.Mcp`** 0.1.3 → 0.1.4 in `Tharga.MongoDB.Mcp`. Patch.
3. **`Tharga.Communication`** 0.1.5 → 0.2.0 in `Tharga.MongoDB.Monitor.Client` and `.Monitor.Server`. Minor on a pre-1.0 package — possibly breaking. Worth verifying whether 0.2.0 ships the upstream that GitHub [#100](https://github.com/Tharga/MongoDB/issues/100) is blocked on (`AllowUnauthenticated` mixed-mode auth + `GetKeyUsage()`). If so, note it but don't pull #100 into this PR — that's a separate consumer-side feature.
4. **`Tharga.Console`** 3.7.4 → 4.1.1 in `ConsoleSample`. Major with breaking change (`ICommand.IsHidden` renamed to `IsVisible` with inverted semantics, per the existing follow-up entry in `Requests.md`). Sample only, so worst case we drop the bump from this PR and file a code-update follow-up.
5. **`SharpCompress`** 0.48.0 → 1.0.0 in `Tharga.MongoDB`. Major. The current pin is a deliberate CVE floor (GHSA-6c8g-7p36-r338, affected ≤ 0.47.4) above MongoDB.Driver's transitive 0.30.1. 1.0.0 stays above the affected range so the floor still holds; the comment in `Tharga.MongoDB.csproj` needs to be updated to reflect the new version. Verify nothing transitive changed (e.g. MongoDB.Driver hasn't started pulling its own ≥ 1.0).

## Out of scope

- `Snappier` 1.3.1 is **not** outdated (current is latest). Pin stays.
- Issue [#100](https://github.com/Tharga/MongoDB/issues/100) (API key auth state surface) — even if `Tharga.Communication` 0.2.0 unblocks it, the consumer-side feature is its own PR.
- Anything that would require behavior changes beyond the bump itself.

## Acceptance criteria

- All targeted bumps applied or explicitly deferred with reason in this file.
- Full `dotnet build -c Release` clean (no new warnings introduced by the bumps).
- `dotnet test -c Release` shows the same Lockable cohort flakiness as bare master and no new failures attributable to the bumps.
- The CVE-pin comment in `Tharga.MongoDB.csproj` updated to reflect the new SharpCompress version (or removed if MongoDB.Driver now floors high enough on its own).
- If `Tharga.Console` 4.1.1 turns out to break `ConsoleSample` non-trivially, defer with a reason recorded.

## Done condition

- Acceptance criteria met.
- PR opened and merged. Plan archived.
- The earlier "SharpCompress 0.48.0 → 1.0.0 deliberately deferred" note in the fingerprint-readonly-properties done-file becomes historical — no follow-up entry needed in the central Requests.md since the deferral is now resolved.

## Validation

Unit tests alone are sufficient — no Eplicta smoke needed (no behavior changes intended).
