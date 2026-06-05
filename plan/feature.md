# Feature: Optional Quilt4Net firewall integration

Closes [#111](https://github.com/Tharga/MongoDB/issues/111) on merge.

## Goal

Add coordination with the [Quilt4Net Atlas firewall proxy](https://www.nuget.org/packages/Quilt4Net.Toolkit) around the connection lifecycle. **The mode is inferred from the configured fields on `MongoDbApiAccess` — there is no enum the consumer sets directly.** The matrix:

| Atlas keys (PublicKey/PrivateKey) | Quilt4Net key | Effective behaviour |
|:--:|:--:|:--|
| ✔ | ✘ | **Classic** — today's behaviour. Direct Atlas open. |
| ✔ | ✔ | **Notify** — direct Atlas open + periodic `ReportUsedAsync` heartbeat so Quilt4Net knows this consumer's IP is in use. |
| ✘ | ✔ | **Open** — Quilt4Net opens the firewall via `OpenAsync`. Subsequent heartbeats also use `OpenAsync` — when the firewall is already open the call simply registers usage (Quilt4Net returns `AlreadyOpen`), so no separate `ReportUsedAsync` is needed. Consumer never holds an Atlas credential. |
| ✘ | ✘ | No firewall management — existing skip behaviour. |

Existing consumers (Atlas keys only) keep their behaviour unchanged. Adding a `Quilt4NetApiKey` switches them to **Notify** automatically; removing the Atlas keys but keeping the Quilt4Net key switches them to **Open**.

## Background

`Tharga.MongoDB` already manages Atlas firewall access via `IMongoDbFirewallService` (calls Atlas REST directly using `MongoDbApiAccess.PublicKey/PrivateKey/GroupId`). `IMongoDbFirewallStateService` caches the open state per-cluster so we don't hit Atlas on every connection.

[Quilt4Net.Toolkit 0.8.0](https://www.nuget.org/packages/Quilt4Net.Toolkit) ships `IAtlasFirewallClient` over Quilt4Net's firewall-proxy endpoints:

- `OpenAsync(ip)` — needs `firewall:manage` scope.
- `CloseAsync(ip)` — needs `firewall:manage` scope.
- `ReportUsedAsync(ip)` — needs `firewall:usage` scope (heartbeat).
- `GetStateAsync(ip)` — needs `firewall:usage` scope.

The factory `IAtlasFirewallClientFactory.Create(AtlasFirewallProxyKeyEntry)` returns a client bound to one Atlas project; the per-call API key + GroupId ride on the entry.

The issue specifies:

1. **Open on demand** — before connecting (or on a connect failure that looks like an IP block), open the firewall for the caller's egress IP, then connect.
2. **Heartbeat while in use** — periodically `ReportUsedAsync` so Quilt4Net keeps the opening alive; when the app stops, heartbeats cease and the sweeper auto-closes.

## Scope

### 1. New optional fields on `MongoDbApiAccess` + an internal mode helper

```csharp
public record MongoDbApiAccess
{
    // Existing — unchanged.
    public string PublicKey { get; init; }
    public string PrivateKey { get; init; }
    public string GroupId { get; init; }
    public string Name { get; init; }

    // New
    public string Quilt4NetBaseUrl { get; init; }
    public string Quilt4NetApiKey { get; init; }
}
```

The mode is inferred at the dispatch site by an internal helper on `MongoDbApiAccessExtensions`:

```csharp
internal enum FirewallMode { None, Classic, Notify, Open }

internal static FirewallMode GetFirewallMode(this MongoDbApiAccess access)
{
    var hasAtlas    = !string.IsNullOrEmpty(access?.PublicKey) && !string.IsNullOrEmpty(access?.PrivateKey);
    var hasQuilt4Net = !string.IsNullOrEmpty(access?.Quilt4NetApiKey);

    if (hasAtlas    && hasQuilt4Net) return FirewallMode.Notify;
    if (!hasAtlas   && hasQuilt4Net) return FirewallMode.Open;
    if (hasAtlas)                    return FirewallMode.Classic;
    return FirewallMode.None;
}
```

The enum stays `internal` — consumers never see it. They only know "supply some combination of keys; we figure it out".

`Quilt4NetBaseUrl` defaults to Quilt4Net's published default (`https://quilt4net.com/`) when not provided — matches the toolkit's own default.

`Quilt4NetApiKey` is the per-bundle entry key. In **Notify** mode it can be a `firewall:usage` key (heartbeat only). In **Open** mode it must be a `firewall:manage` key (open + heartbeat). The toolkit's `AtlasFirewallProxyKeyEntry.CanManage` lets us assert at runtime if a wrong-scope key is supplied for Open mode — fail clear and early.

New `DatabaseOptions.Quilt4NetHeartbeatInterval` (`TimeSpan?`, default `TimeSpan.FromMinutes(5)`) for the heartbeat cadence. Set to `null` to disable the heartbeat service entirely.

### 2. Quilt4Net.Toolkit dependency in core

Add `<PackageReference Include="Quilt4Net.Toolkit" Version="0.8.0" />` to `Tharga.MongoDB.csproj`. Per user decision: integration lives in the core package, not a separate add-on, since Atlas integration is already a core concern.

### 3. `Quilt4NetFirewallService` — thin wrapper over `IAtlasFirewallClient`

```csharp
internal sealed class Quilt4NetFirewallService
{
    private readonly IAtlasFirewallClientFactory _factory;

    public Task<FirewallOpenResult> OpenAsync(MongoDbApiAccess access, IPAddress ip, CancellationToken ct);
    public Task<FirewallUsageResult> ReportUsedAsync(MongoDbApiAccess access, IPAddress ip, CancellationToken ct);
}
```

Constructs an `AtlasFirewallProxyKeyEntry` from `MongoDbApiAccess.Quilt4NetApiKey`/`GroupId` and calls the factory.

### 4. Mode dispatch in `MongoDbFirewallStateService`

Refactor `AssureFirewallAccessAsync` to call `accessInfo.GetFirewallMode()` and dispatch:

- **None**: existing skip (`return "No information."`).
- **Classic**: existing behaviour — direct `IMongoDbFirewallService.AssureFirewallAccessAsync` only. No Quilt4Net work.
- **Notify**: existing direct call + register `(access, ip)` with the heartbeat service so a usage report goes out on the next tick.
- **Open**: skip the direct call entirely; call `Quilt4NetFirewallService.OpenAsync` instead; register for heartbeats.

The per-access cache (`_dictionary`) gates all four so the open call only fires once per process per access (unless `force: true` or the IP changed).

### 5. `Quilt4NetHeartbeatService : BackgroundService`

- Holds a thread-safe `ConcurrentDictionary<(MongoDbApiAccess, IPAddress), FirewallMode>` of active tuples. The mode is part of the value so the tick knows which heartbeat method to use.
- Registered as a hosted service when `DatabaseOptions.Quilt4NetHeartbeatInterval != null`. Always register if non-null — the loop is dormant when the dictionary is empty (same pattern as `FailedIndexRecheckService`).
- Each tick: snapshot the dictionary and, per entry, call:
  - **Notify** mode → `Quilt4NetFirewallService.ReportUsedAsync(access, ip)` — pure usage signal, the firewall was opened directly via Atlas.
  - **Open** mode → `Quilt4NetFirewallService.OpenAsync(access, ip)` — doubles as both open (first time) and usage signal (subsequent calls return `AlreadyOpen`). No separate `ReportUsedAsync` needed.
- Failures are logged at debug level and don't remove the entry (transient HTTP failures shouldn't cause a silent stop).
- `AuthorizationException` (specifically `AtlasFirewallAuthorizationException`) DOES remove the entry — that's a configuration error, retrying won't help.

### 6. `MongoDbService` connect-failure fallback (later, if needed)

The issue mentions "on a connect failure that looks like an IP block, also open." This is harder to plumb cleanly through MongoDB.Driver's connection layer. **Deferred** — the proactive `AssureFirewallAccessAsync` covers the happy path; the connect-failure fallback is a follow-up if a real consumer hits a case the proactive path doesn't catch.

## Out of scope

- **Connect-failure fallback open** — proactive open is enough for MVP. Reconsider if a consumer hits a connect that's blocked despite their config.
- **Auto-close on dispose** — let Quilt4Net's sweeper handle it via heartbeat absence. The issue explicitly allows this default. `CloseAsync` is in the wrapper API but not called by the state machine; consumers who need explicit close can call it.
- **Value-group bundle delivery** — direct config (`Quilt4NetApiKey` field) only. Bundle integration (`IValueGroupClient.GetAsync(...)`) is a separate, larger feature. Consumers who want it can fetch the bundle externally and supply the resulting key.
- **CIDR opening** — single IP only (from `IExternalIpAddressService`). The toolkit method accepts CIDR but the typical home/office egress IP scenario doesn't need it.
- **Per-cluster heartbeat interval override** — single global interval via `DatabaseOptions`. Per-access overrides if a real consumer needs them.

## Acceptance criteria

- `MongoDbApiAccess.Mode = FirewallMode.Classic` (the default) keeps every existing consumer's behaviour: direct Atlas open, no Quilt4Net work, no new dependencies invoked at runtime.
- `Mode = FirewallMode.Notify` performs the existing direct Atlas open AND queues a `ReportUsedAsync` for the egress IP. Heartbeats land on the configured interval.
- `Mode = FirewallMode.Open` skips the direct Atlas API entirely and calls `Quilt4NetFirewallService.OpenAsync` instead. Heartbeats land on the configured interval.
- The heartbeat `BackgroundService` is dormant (no logs, no Mongo work) when no access is in `Notify`/`Open` mode.
- An invalid Quilt4Net key (`AtlasFirewallAuthorizationException` on heartbeat) removes the entry — the loop doesn't burn cycles retrying a misconfigured key.
- Existing tests stay green (modulo the pre-existing Lockable transaction-test cohort).
- New tests cover each mode's dispatch + the heartbeat add/remove behaviour.

## Done condition

- Acceptance criteria met.
- Plan archived to `done/quilt4net-firewall.md`; `planned/README.md` updated.
- PR opens with `closes #111`.

## Effort

Medium. ~6 production-code touchpoints (enum, options, wrapper service, mode dispatch in state service, heartbeat BackgroundService, registration), plus tests covering each mode + the heartbeat lifecycle. Estimate ~3–5 days; one cohesive PR.

## NuGet

- New runtime dependency on **Quilt4Net.Toolkit 0.8.0** for `Tharga.MongoDB.csproj`.
- Targets match (`net8.0;net9.0;net10.0`).
- No version bump on Tharga.MongoDB itself in this PR (publisher bumps on release).
