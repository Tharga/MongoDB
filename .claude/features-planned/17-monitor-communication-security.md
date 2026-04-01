# Feature: Secure communication between Monitor Client and Server

## Source
Security requirement for distributed monitoring

## Goal
Protect the SignalR communication channel between monitoring agents and the central server so that only authorized agents can connect and send data.

## Design Decision
- **Shared API key** sent as a custom header during SignalR negotiation
- Server accepts primary + secondary key for zero-downtime key rotation
- No keys configured = all connections accepted (backwards compatible)
- Authentication built entirely in Tharga.Communication — no middleware in this package
- Configuration via `appsettings.json`, Manage User Secrets, environment variables, or code

## Dependencies
- ~~**Blocked on:** Tharga.Communication API key authentication feature~~ **DONE** — API key auth is implemented in Tharga.Communication v1.1.0

## Remaining Work
- Document how to configure API keys for Monitor.Client and Monitor.Server
- Verify that the Tharga.Communication API key auth works end-to-end with the monitor packages
- Update README

## Acceptance Criteria
- [x] Tharga.Communication supports API key auth (primary + secondary)
- [ ] Unauthorized agents cannot connect to the monitor hub
- [ ] Configuration works via appsettings.json, User Secrets, or code
- [ ] Existing functionality works without breaking changes

## Done Condition
The monitor hub rejects unauthorized connections. Key rotation is possible without downtime.
