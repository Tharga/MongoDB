# Plan: Fix firewall opening and monitor database access

## Steps

### Step 1: Reorder startup — firewall before monitor
- [x] Move firewall opening before monitor.Start() in UseMongoDB
- [x] Make firewall blocking when monitor is enabled
- [x] Build and verify

### Step 2: Route monitor collection through firewall
- [x] Change MongoDbCollectionCache to call AssureFirewallAccessAsync before DB access
- [x] Build and verify

### Step 3: Clear firewall status logging
- [x] Log at Information level for success
- [x] Log at Error level for failure (per config, with host name)
- [x] Log summary after all configs (opened, already open, skipped, failed)
- [x] Build and verify

### Step 4: Tests and commit
- [x] Build and test (258 passed)
- [ ] Commit all changes
