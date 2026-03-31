# Feature: Tharga.MongoDB.Monitor.Client package

## Source
Architecture — distributed monitoring (feature #11)

## Goal
Create a NuGet package that allows remote agents to forward monitoring data to a central server via Tharga.Communication. Agents that already use Tharga.MongoDB get monitoring forwarding by adding this package and configuring a server URL.

## Originating Branch
develop

## Scope
- New project `Tharga.MongoDB.Monitor.Client`
- Subscribes to `CallStartEvent`/`CallEndEvent` from `MongoDbServiceFactory`
- Forwards `CallDto` to a remote server via Tharga.Communication's `PostAsync`
- Configurable via `builder.AddMongoDB(o => o.Monitor.SendTo("https://server/hub"))`
- Handles reconnection (provided by Tharga.Communication)
- DTOs live in the Client package; the future Server package will reference it

## Out of Scope (deferred to Feature #12 — Monitor.Server)
- Server-side SignalR hub that receives data
- Aggregation of remote + local data into IDatabaseMonitor
- Subscription-based "only send when someone is watching" (depends on Tharga.Communication subscription feature)

## Acceptance Criteria
- [ ] New project `Tharga.MongoDB.Monitor.Client` builds and is included in solution
- [ ] Package references `Tharga.Communication` for transport
- [ ] Subscribes to `CallStartEvent`/`CallEndEvent` and forwards `CallDto` via `PostAsync`
- [ ] Configurable via `MonitorOptions.SendTo` (server URL)
- [ ] No data sent when `SendTo` is not configured
- [ ] Zero impact on agent performance when not configured
- [ ] Tests cover: forwarding logic, configuration, no-op when disabled
- [ ] Feature request submitted to Tharga.Communication for subscription support

## Done Condition
A remote agent using Tharga.MongoDB can opt in to forwarding monitoring data to a central server by adding this package and one line of configuration.
