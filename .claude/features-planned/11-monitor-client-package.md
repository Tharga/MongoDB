# Feature: Tharga.MongoDB.Monitor.Client package

## Source
Architecture — distributed monitoring

## Goal
Create a NuGet package that allows remote agents to send monitoring data to a central server via SignalR. Agents that already use Tharga.MongoDB get monitoring forwarding by adding this package and configuring a server URL.

## Scope
- New project `Tharga.MongoDB.Monitor.Client`
- Subscribes to the same `CallStartEvent`/`CallEndEvent` from `MongoDbServiceFactory`
- Forwards `CallDto` and related data to a SignalR hub
- Configurable via `builder.AddMongoDB(o => o.Monitor.SendTo("https://server/monitor-hub"))`
- Handles reconnection and buffering when the server is unavailable
- Only sends data when the server signals that someone is subscribed/watching
- Evaluate whether to use `Tharga.Communication` as transport or a standalone SignalR hub

## Dependencies
- Feature #09 (monitor-source-flag) must be completed first — data needs source tagging before transport

## Acceptance Criteria
- [ ] Package builds and publishes independently
- [ ] Agent can forward monitoring data to a remote server
- [ ] No data sent when no one is watching (subscription-based)
- [ ] Reconnection works after server restarts
- [ ] Zero impact on agent performance when server is not configured
- [ ] Tests cover connection lifecycle and data forwarding

## Done Condition
A remote agent using Tharga.MongoDB can opt in to forwarding monitoring data to a central server by adding this package and one line of configuration.
