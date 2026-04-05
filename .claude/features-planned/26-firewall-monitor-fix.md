# Feature: Fix firewall opening and monitor database access

## Source
Bug report — startup fails when firewall is locked by IP

## Goal
Ensure the firewall is open before any database access (including the monitor's `_monitor` collection), and provide clear logging of firewall success/failure.

## Bugs

### 1. Monitor starts before firewall opens
In `UseMongoDB`, `monitor.Start()` (line 269) runs synchronously and blocks on `_cache.LoadAsync()` which reads the `_monitor` collection. Firewall opening runs after that (line 275) in a background task. If the firewall is IP-locked, the monitor fails because the firewall hasn't opened yet.

### 2. Monitor bypasses firewall check
`MongoDbCollectionCache` accesses `BaseMongoDatabase` directly via `IMongoDbServiceInternal.BaseMongoDatabase`. This never calls `AssureFirewallAccessAsync()`. Regular collections go through `IMongoDbService.GetCollectionAsync<T>()` which checks the firewall. The monitor should use the same path.

### 3. No clear logging of firewall status
Firewall success/failure logs are inside a background task at Debug level. Operators need to see at startup whether the firewall opened or failed.

## Scope

### Fix 1: Reorder startup — firewall before monitor
- Move firewall opening **before** `monitor.Start()` in `UseMongoDB`
- Make firewall opening synchronous (blocking) when monitor is enabled, since monitor needs DB access
- Keep async/background option for non-monitor scenarios via `WaitToComplete`

### Fix 2: Route monitor collection through firewall
- Change `MongoDbCollectionCache.GetBaseDatabase()` to use `IMongoDbService.GetCollectionAsync` instead of `BaseMongoDatabase`
- Or call `AssureFirewallAccessAsync()` before accessing `BaseMongoDatabase`
- Ensure the `_monitor` collection benefits from the same firewall handling as regular collections

### Fix 3: Log firewall status clearly
- Log firewall open result at Information level (not Debug)
- Log "Firewall opened for {host}" or "Firewall already open for {host}" on success
- Log "Firewall open FAILED for {host}: {error}" at Error level on failure
- Log summary after all configs processed: "Firewall: {n} opened, {m} already open, {k} failed"

## Acceptance Criteria
- [ ] Monitor starts after firewall is open
- [ ] Monitor `_monitor` collection access goes through firewall check
- [ ] Firewall status logged at Information/Error level
- [ ] Startup works when firewall is IP-locked
- [ ] Tests pass
- [ ] No breaking changes for existing setups

## Done Condition
Applications with IP-locked MongoDB firewalls can start successfully with monitor enabled. Firewall status is visible in logs.
