# Tharga.MongoDB

A MongoDB repository toolkit for .NET 8 / 9 / 10. Adds dynamic database/collection naming, automatic index assurance, document-level locking with commit-on-success semantics, multi-document transactions, keyset pagination, and built-in monitoring of every call (latency, slow queries, cache stats) on top of the official `MongoDB.Driver`.

## Install

```
dotnet add package Tharga.MongoDB
```

```csharp
builder.AddMongoDB();
```

Configure connection strings in `appsettings.json`:

```json
"ConnectionStrings": {
  "Default": "mongodb://localhost:27017/MyApp{Environment}{Part}"
}
```

Define an entity and a repository collection — both get auto-registered into DI:

```csharp
public record Order : EntityBase<ObjectId>
{
    public string CustomerName { get; init; }
    public DateTime CreatedAt { get; init; }
}

public class OrderCollection : DiskRepositoryCollectionBase<Order, ObjectId>
{
    public OrderCollection(IMongoDbServiceFactory factory) : base(factory) { }
    public override string CollectionName => "orders";
}
```

Inject `IRepositoryCollection<Order, ObjectId>` (or your concrete collection) wherever you need it.

## What's in the box

- **Repository pattern** with `Disk` and `Lockable` collection bases. Lockable adds per-document optimistic locks with commit/release/error workflows so you can hand a document to a worker, let it crash, and have the lock auto-recover.
- **Dynamic database & collection naming** via `DatabaseContext` — multi-tenant URLs like `mongodb://.../{Environment}{Part}` resolve at call time, not at startup.
- **Automatic index assurance** with rename-safe modes (`ByName`, `BySchema`, `DropCreate`).
- **Multi-document transactions** via `WithTransactionAsync` — session-aware writes on both Disk and Lockable; lockable leases support transactional commit so the document update and your business writes land atomically.
- **Keyset pagination** — `GetPageAsync` / `GetPageProjectionAsync` with opaque `CursorToken`. O(log N) per page regardless of depth, no skip penalty. `CursorPager<TEntity, TKey>` adapter for grids that emit `(skip, pageSize)`.
- **Monitoring** — every call (with filter, sort, explain plan) is captured and exposed via `IDatabaseMonitor` for live dashboards. Slow-query detection, queue metrics, cache stats, and per-collection status.
- **Result limiter** — hard cap on returned document counts via `ResultLimit` config.
- **Execute limiter** — auto-sized per connection pool to keep long-running streams from oversubscribing the driver.
- **Companion packages**: [`Tharga.MongoDB.Blazor`](https://www.nuget.org/packages/Tharga.MongoDB.Blazor) for the admin UI, [`Tharga.MongoDB.Mcp`](https://www.nuget.org/packages/Tharga.MongoDB.Mcp) for MCP/AI integration, [`Tharga.MongoDB.Monitor.Client`](https://www.nuget.org/packages/Tharga.MongoDB.Monitor.Client) + [`.Server`](https://www.nuget.org/packages/Tharga.MongoDB.Monitor.Server) for centralised monitoring across multiple agents.

## Documentation

Full docs, configuration reference, and worked examples for each feature: [github.com/Tharga/MongoDB](https://github.com/Tharga/MongoDB).

[![GitHub repo](https://img.shields.io/github/repo-size/Tharga/MongoDB?style=flat&logo=github&logoColor=red&label=Repo)](https://github.com/Tharga/MongoDB)
