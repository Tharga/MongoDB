---
_layout: landing
---

# Tharga.MongoDB

A MongoDB repository toolkit for **.NET 8 / 9 / 10**. Built on `MongoDB.Driver`, with first-class support for dynamic database/collection naming, automatic index assurance, document-level locking, multi-document transactions, keyset pagination, and built-in call monitoring.

## Packages

| Package | What it does |
|---|---|
| [Tharga.MongoDB](https://www.nuget.org/packages/Tharga.MongoDB) | Core repository toolkit. Disk + Lockable collection bases, monitoring, transactions, keyset pagination. |
| [Tharga.MongoDB.Blazor](https://www.nuget.org/packages/Tharga.MongoDB.Blazor) | Drop-in Razor admin UI components (collections, calls, clients, queue, indexes). |
| [Tharga.MongoDB.Mcp](https://www.nuget.org/packages/Tharga.MongoDB.Mcp) | MCP (Model Context Protocol) provider — exposes monitoring + actions to AI clients. |
| [Tharga.MongoDB.Monitor.Client](https://www.nuget.org/packages/Tharga.MongoDB.Monitor.Client) | Forwards monitoring data from a remote agent to a central server. |
| [Tharga.MongoDB.Monitor.Server](https://www.nuget.org/packages/Tharga.MongoDB.Monitor.Server) | Receives monitoring data from agents and aggregates into the local monitor. |

## Quick start

```
dotnet add package Tharga.MongoDB
```

```csharp
builder.AddMongoDB();
```

```json
"ConnectionStrings": {
  "Default": "mongodb://localhost:27017/MyApp{Environment}{Part}"
}
```

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

## Where next

- **[Articles](articles/index.md)** — feature guides: getting started, lockable docs, transactions, pagination, monitoring, MCP
- **[API reference](xref:Tharga.MongoDB)** — every public type, method, and option, generated from XML doc comments
- **[GitHub](https://github.com/Tharga/MongoDB)** — source, issues, releases
