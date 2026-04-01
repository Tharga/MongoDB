# Plan: Remote Action Delegation

## Steps

### Step 1: Add SourceName to client connection tracking
- [ ] Add `SourceName` to `MonitorClientConnectionInfo` and `MonitorClientDto`
- [ ] Set `SourceName` on the server when collection info is received from a client
- [ ] Add lookup: `FindConnectionIdBySource(string sourceName)` on `DatabaseMonitor`
- [ ] Build and verify

### Step 2: Create shared action message DTOs
- [ ] Create request/response records for Touch, DropIndex, RestoreIndex, Clean
- [ ] Place in Monitor.Client (shared between client and server via dependency)
- [ ] Build and verify

### Step 3: Implement client-side action handlers
- [ ] Add `SendMessageHandlerBase<TouchRequest, TouchResponse>` etc. on Monitor.Client
- [ ] Each handler executes the action via local `IDatabaseMonitor` and returns result
- [ ] Register handlers
- [ ] Build and verify

### Step 4: Implement server-side delegation in DatabaseMonitor
- [ ] In Touch/DropIndex/RestoreIndex/Clean: detect if collection is remote-only
- [ ] If remote: find connected agent by source, send via `IServerCommunication.SendMessageAsync`
- [ ] If local: execute directly (existing behavior)
- [ ] Build and verify

### Step 5: Tests, README, and commit
- [ ] Write tests
- [ ] Update README
- [ ] Final build and test
- [ ] Commit all changes
