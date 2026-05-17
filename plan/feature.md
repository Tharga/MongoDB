# Feature: Monitor auth surface

Closes [#96](https://github.com/Tharga/MongoDB/issues/96) entirely. References [#100](https://github.com/Tharga/MongoDB/issues/100) — closes Asks #1 + #2; Ask #3 (per-key last-used panel) is a separate planned follow-up.

## Goal

Make the monitor server's auth seam pluggable, drop the leftover primary/secondary terminology (Tharga.Communication 0.2.0 removed it upstream), and give operators a small visual signal in `ClientsView` showing which agents connected with an API key.

## Background

After PR #104, `Tharga.MongoDB.Monitor.Server` consumes `Tharga.Communication` 0.2.0 with the default validator (string equality against `CommunicationOptions.ApiKeys`). That's fine for "all agents share one secret" but doesn't help:

- **Eplicta** plans mixed-mode: legacy agents stay unauthenticated, newer agents get unique per-agent secrets validated against the Aggregator's own key store.
- The operator looking at `ClientsView` can see *that* an agent is connected, but not *whether* it presented a key — a real diagnostic gap once mixed-mode is in flight ("did agent X actually pick up its rotated key?").

Tharga.Communication 0.2.0 provides everything needed (`IApiKeyValidator`, `ApiKeyValidationResult.KeyId`/`KeyName`, `IClientConnectionInfo.KeyId`/`KeyName`). This feature exposes that seam through Tharga.MongoDB's surface and surfaces the result.

## Scope

### 1. Public API on `Tharga.MongoDB.Monitor.Server`

Drop primary/secondary terminology — replace with an `ApiKeys` array and a validator-override seam, mirroring how Tharga.Communication 0.2.0 shapes things.

- **New canonical overload**: `AddMongoDbMonitorServer(this WebApplicationBuilder builder, Action<MongoDbMonitorOptions> configure)`.
- **New `MongoDbMonitorOptions`**:
  - `string[] ApiKeys { get; set; }` — direct array of accepted keys, forwarded to `CommunicationOptions.ApiKeys`.
  - `void UseApiKeyValidator<TValidator>() where TValidator : class, IApiKeyValidator` — forwards to `CommunicationOptions.RegisterApiKeyValidator<TValidator>()`. Closes #96.
- **Old `(primaryApiKey, secondaryApiKey)` overload** stays one release with `[Obsolete]`, internally folds the two strings into the new `ApiKeys` array. Eplicta and any other consumer compiles unchanged with a deprecation warning. Removed next major.

### 2. Auth status plumbing

`MonitorClientConnectionInfo.KeyId` / `KeyName` (currently no-op defaults from PR #104) start carrying real values from the connection's `IClientConnectionInfo`. A new field `AuthKeyName` on `MonitorClientDto` surfaces this through to the Blazor layer — non-null for keyed connections, null for unauth.

### 3. Seal badge in `ClientsView`

One small column (or inline icon — design call at impl time):

- Keyed connection → seal/lock icon. Tooltip shows `AuthKeyName` (or `"(unnamed key)"` if the validator didn't supply one).
- Unauth connection → empty cell.
- Whole column hidden when zero agents in the current view are keyed (so pure-legacy deployments see no visual change).

## Out of scope

- **Per-key last-used panel** (#100 Ask #3 — verifying rotation by watching `GetKeyUsage()` timestamps). Will live in `planned/monitor-key-last-used-panel.md` after close-out. Becomes valuable once Eplicta is well into the per-agent-key rotation phase.
- **Claim plumbing per key** (e.g. scope tags). Only `KeyName` for display in this PR.
- **Validator behavior changes** in Eplicta — that's Eplicta-side work consuming this PR's `UseApiKeyValidator<T>()` seam.

## Acceptance criteria

- Consumers can register their own `IApiKeyValidator` via `MongoDbMonitorOptions.UseApiKeyValidator<T>()`.
- `MonitorClientDto.AuthKeyName` is non-null when the connection presented a valid key (and the validator returned a non-null `KeyName`); null otherwise.
- `ClientsView` shows a seal badge only on rows for keyed agents, and the badge column auto-hides when no agent in the current rows is keyed.
- Old `(primaryApiKey, secondaryApiKey)` overload still compiles and behaves identically (folds into `ApiKeys`); compiler emits an obsolete warning.
- Existing unit tests + new tests for the validator forwarding and `AuthKeyName` plumbing pass.

## Done condition

- Acceptance criteria met.
- `done/monitor-auth-surface.md` archived; planned-queue updated.
- `planned/monitor-key-last-used-panel.md` filed as the follow-up.
- PR opened and merged.

## Validation

Unit tests for API forwarding + DTO plumbing. Eplicta smoke optional — useful as a sanity check, but the feature works against any consumer once the Eplicta-side validator is written (which is Eplicta's own work).

## NuGet

`Tharga.Communication` is already at 0.2.0 from PR #104. No bumps needed.
