# Plan: Optional Quilt4Net firewall integration

Feature scope: see [feature.md](feature.md). Branch: `feature/quilt4net-firewall`. Closes #111.

## Steps

### Phase 1 — Config + mode helper

- [ ] **1.1** Add `Quilt4NetBaseUrl` and `Quilt4NetApiKey` fields to `MongoDbApiAccess`. Document each via XML doc (manage vs usage scope hint, default base URL).
- [ ] **1.2** Add `internal enum FirewallMode { None, Classic, Notify, Open }`.
- [ ] **1.3** Add `internal static FirewallMode GetFirewallMode(this MongoDbApiAccess)` extension on `MongoDbApiAccessExtensions` (existing file).
- [ ] **1.4** Add `TimeSpan? Quilt4NetHeartbeatInterval { get; set; } = TimeSpan.FromMinutes(5);` to `DatabaseOptions` (or omit and use a constant — confirm at impl based on whether DatabaseOptions hosts other Atlas/firewall knobs today; if not, prefer a constant unless the user asks for runtime tuning).
- [ ] **1.5** Tests on `MongoDbApiAccessExtensionsTests` (or a new `FirewallModeTests`): 4 cases for the truth table — None / Classic / Notify / Open.

### Phase 2 — Quilt4Net.Toolkit dependency + registration

- [ ] **2.1** Add `<PackageReference Include="Quilt4Net.Toolkit" Version="0.8.0" />` to `Tharga.MongoDB.csproj`. Verify net8/9/10 multi-target alignment.
- [ ] **2.2** Wire `services.AddQuilt4NetAtlasFirewallClient(...)` from `AddMongoDB`. Always register the factory (cheap — it's `services.AddHttpClient` + a singleton factory); per-call key comes from the `MongoDbApiAccess`.
- [ ] **2.3** If the user wants `Quilt4NetBaseUrl` overridable per-access, the factory pattern needs work — the toolkit's `AddQuilt4NetAtlasFirewallClient` takes a single base URL at registration. **Open question**: if multiple `MongoDbApiAccess` records use different `Quilt4NetBaseUrl`s, we'd need a factory-of-factories. Resolve at impl — most consumers will have one Quilt4Net server. Document the single-server limitation if we ship it as-is.

### Phase 3 — `Quilt4NetFirewallService` wrapper

- [ ] **3.1** New `Tharga.MongoDB/Atlas/Quilt4NetFirewallService.cs` (internal):
  ```csharp
  internal sealed class Quilt4NetFirewallService
  {
      private readonly IAtlasFirewallClientFactory _factory;
      public Quilt4NetFirewallService(IAtlasFirewallClientFactory factory) { _factory = factory; }

      public Task<FirewallOpenResult> OpenAsync(MongoDbApiAccess access, IPAddress ip, CancellationToken ct = default);
      public Task<FirewallUsageResult> ReportUsedAsync(MongoDbApiAccess access, IPAddress ip, CancellationToken ct = default);
  }
  ```
  Constructs `AtlasFirewallProxyKeyEntry` per call from `access.Quilt4NetApiKey`/`access.GroupId`. Set `CanManage = true` for Open mode callers, `false` for Notify-mode usage-only callers (we know which mode dispatched).
- [ ] **3.2** Register as transient in `AddMongoDB` (next to existing Atlas services).
- [ ] **3.3** Test with a mocked `IAtlasFirewallClientFactory` — verify the entry shape sent to `Create()` matches the access record.

### Phase 4 — Mode dispatch in `MongoDbFirewallStateService`

- [ ] **4.1** Inject `Quilt4NetFirewallService`, `IExternalIpAddressService`, and `Quilt4NetHeartbeatService` into `MongoDbFirewallStateService`.
- [ ] **4.2** Refactor `AssureFirewallAccessAsync` to switch on `accessInfo.GetFirewallMode()`:
  - **None** → existing `"No information."` early return.
  - **Classic** → existing direct path. No change.
  - **Notify** → existing direct path + `_heartbeat.Register(accessInfo, egressIp)`.
  - **Open** → `_quilt4Net.OpenAsync(accessInfo, egressIp)` + `_heartbeat.Register(accessInfo, egressIp)`. Skip direct.
- [ ] **4.3** Cache (`_dictionary`) keyed on `MongoDbApiAccess` covers all four — the open call fires once per process unless `force: true` or the IP changed (existing behaviour).
- [ ] **4.4** Tests: a `MongoDbFirewallStateServiceTests` with mocks for each branch.

### Phase 5 — `Quilt4NetHeartbeatService : BackgroundService`

- [ ] **5.1** New `Tharga.MongoDB/Atlas/Quilt4NetHeartbeatService.cs`:
  ```csharp
  internal sealed class Quilt4NetHeartbeatService : BackgroundService
  {
      private readonly Quilt4NetFirewallService _firewall;
      private readonly ILogger<Quilt4NetHeartbeatService> _logger;
      private readonly TimeSpan _interval;
      // Mode in the value so the tick dispatches Open vs Notify correctly.
      private readonly ConcurrentDictionary<(MongoDbApiAccess, IPAddress), FirewallMode> _active = new();

      public void Register(MongoDbApiAccess access, IPAddress ip, FirewallMode mode);
      public void Unregister(MongoDbApiAccess access, IPAddress ip);

      protected override async Task ExecuteAsync(CancellationToken ct)
      {
          while (!ct.IsCancellationRequested)
          {
              await Task.Delay(_interval, ct);
              if (_active.IsEmpty) continue; // Dormant — no log noise, no work.

              foreach (var ((access, ip), mode) in _active)
              {
                  try
                  {
                      if (mode == FirewallMode.Open)
                      {
                          // OpenAsync doubles as heartbeat when already open (returns AlreadyOpen).
                          await _firewall.OpenAsync(access, ip, ct);
                      }
                      else // Notify
                      {
                          await _firewall.ReportUsedAsync(access, ip, ct);
                      }
                  }
                  catch (AtlasFirewallAuthorizationException ex)
                  {
                      _logger.LogWarning(ex, "Quilt4Net heartbeat: auth rejected for {Group}/{Ip} — removing from heartbeat loop.", access.GroupId, ip);
                      _active.TryRemove((access, ip), out _);
                  }
                  catch (Exception ex)
                  {
                      _logger.LogDebug(ex, "Quilt4Net heartbeat: transient failure for {Group}/{Ip}.", access.GroupId, ip);
                  }
              }
          }
      }
  }
  ```
- [ ] **5.2** Register as singleton + hosted service when `Quilt4NetHeartbeatInterval != null` (always, by default; same pattern as `FailedIndexRecheckService`).
- [ ] **5.3** Tests: `Quilt4NetHeartbeatServiceTests` covering Register/Unregister, dormant-when-empty, auth-error removes entry, transient-error keeps entry, Open-mode entry dispatches to OpenAsync, Notify-mode entry dispatches to ReportUsedAsync.

### Phase 6 — Close-out

- [ ] **6.1** Single cohesive commit.
- [ ] **6.2** Push.
- [ ] **6.3** Archive plan to `done/quilt4net-firewall.md`, update `planned/README.md` Done section, `git rm -r plan`, final commit `feat: quilt4net-firewall complete`, push, open PR closing #111.

## Last session

Plan finalised after issue review + design discussion. User chose: **add to Tharga.MongoDB core** (not a separate package), **mode inferred from config fields** on `MongoDbApiAccess` (no explicit enum exposed), three modes: **Classic** (today, direct Atlas only), **Notify** (direct + Quilt4Net heartbeat), **Open** (Quilt4Net only). Six phases; first five are the production-code touchpoints; #6 is close-out. Awaiting go-ahead to start Phase 1.

## Open questions worth flagging at impl

- **Per-access `Quilt4NetBaseUrl` override** — the toolkit's `AddQuilt4NetAtlasFirewallClient` takes a single base URL at registration. If we want per-access overrides, we'd need a factory-of-factories. Most consumers have one Quilt4Net server; ship with one URL globally and document the limitation. (Plan 2.3.)
- **`Quilt4NetHeartbeatInterval` location** — `DatabaseOptions` vs constant. If the user never needs to tune it, a constant is cleaner. If they do, `DatabaseOptions`. (Plan 1.4.)
- **`CanManage` enforcement in Open mode** — should we throw eagerly if `Quilt4NetApiKey` is supplied for Open mode and turns out to be a usage-only key? Toolkit's `AtlasFirewallProxyKeyEntry.CanManage` exposes the flag, but consumers may not know it. Prefer: let the open call fail with `AtlasFirewallAuthorizationException` and log the actionable error. (Plan 3.1.)
- **Heartbeat ordering** — `Register` happens during the cache-gated `AssureFirewallAccessAsync`, so the same `(access, ip)` won't be registered twice unless the IP changed. Confirm: when the IP changes (egress IP rotation), do we unregister the old entry? Probably yes; verify at impl.
- **Unregister timing** — when does an entry leave the heartbeat loop? Auth error: immediately. Otherwise: never (stays until process exit). Add explicit `Unregister` for completeness even though no current caller uses it.
