# Feature: Tharga.MongoDB.Mcp

## Originating Branch
develop

## Goal
Expose MongoDB monitoring data via MCP (Model Context Protocol) so AI agents can query collections, monitoring, and connected clients, plus trigger touch/rebuild actions.

## Source
- MCP master plan Phase 2 (`c:\Users\danie\SynologyDrive\Documents\Notes\Tharga\plans\Mcp\plan.md`)
- Pending request in `Requests.md` from all products

## Scope
- New project `Tharga.MongoDB.Mcp` (new NuGet package)
- References `Tharga.Mcp` (foundation) and `Tharga.MongoDB` (monitoring data source)
- Provides `AddMcpMongoDB()` extension on `IThargaMcpBuilder`
- All data/actions scoped to `McpScope.System` (infrastructure, not team-specific)

## Resources to expose
- `mongodb.collections` — list of collections with status, index info, document count
- `mongodb.monitoring` — recent calls, latency, slow queries
- `mongodb.clients` — connected monitor agents (for distributed monitoring)

## Tools to expose
- `mongodb.touch` — refresh collection stats
- `mongodb.rebuild_index` — restore/rebuild indexes

## Acceptance Criteria
- [ ] `Tharga.MongoDB.Mcp` package builds
- [ ] `AddMcpMongoDB()` extension registers provider(s)
- [ ] Resources return current data from `IDatabaseMonitor`
- [ ] Tools execute via `IDatabaseMonitor.TouchAsync` / `RestoreIndexAsync`
- [ ] All exposed on System scope
- [ ] CI/CD pipeline packs the new package
- [ ] Tests cover providers
- [ ] README documents usage

## Done Condition
An AI agent can list collections, see slow queries, and trigger rebuilds via MCP — with full audit trail (when Platform.Mcp is registered).
