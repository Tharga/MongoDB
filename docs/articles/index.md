# Articles

Feature guides for `Tharga.MongoDB`. Skim the [Getting started](getting-started.md) page first if you're new — it gives you a runnable end-to-end example. Otherwise jump to whichever topic matches what you're solving:

- **[Getting started](getting-started.md)** — install, configure, register a collection, the first read/write
- **[Lockable collections](lockable-collections.md)** — per-document optimistic locking with commit/release/error workflows
- **[Transactions](transactions.md)** — multi-document atomicity with `WithTransactionAsync` and lockable lease commits
- **[Keyset pagination](keyset-pagination.md)** — `GetPageAsync` / `CursorPager` for grids that need O(log N) per page
- **[Monitoring](monitoring.md)** — the `IDatabaseMonitor` surface, slow-query detection, and centralised topologies via `Monitor.Client` / `Monitor.Server`
- **[MCP integration](mcp-integration.md)** — exposing collections, monitoring, and admin actions to AI clients via `Tharga.MongoDB.Mcp`

Each guide is short — the full reference is in the [API](xref:Tharga.MongoDB) tab.
