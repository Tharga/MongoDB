# Feature: Default port and URL configuration for Monitor Client/Server

## Source
Configuration improvement for distributed monitoring

## Goal
Simplify configuration by having a default port that both Monitor.Client and Monitor.Server agree on, while still allowing overrides on both sides.

## Scope
- Define a default monitor port (e.g. 7210) as a shared constant
- `AddMongoDbMonitorServer()` optionally accepts a port/URL; defaults to the shared port
- `AddMongoDbMonitorClient()` optionally accepts a server URL; defaults to `https://localhost:{default-port}`
- Both sides allow full override via parameters or configuration
- Zero-config scenario: `builder.AddMongoDbMonitorServer()` + `builder.AddMongoDbMonitorClient()` just work on localhost

## Acceptance Criteria
- [ ] Default port defined as a constant accessible by both packages
- [ ] Server listens on default port when no override is provided
- [ ] Client connects to default port when no `sendTo` is provided
- [ ] Both sides allow full URL/port override
- [ ] Sample projects updated to use defaults

## Done Condition
`AddMongoDbMonitorServer()` and `AddMongoDbMonitorClient()` work out of the box on localhost without explicit URL configuration.
