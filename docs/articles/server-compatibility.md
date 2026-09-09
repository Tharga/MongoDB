# Server compatibility

`Tharga.MongoDB` talks to MongoDB through `MongoDB.Driver`, so the range of servers it can reach is decided by the driver version it carries. From **2.16.0** that driver is held at **3.9.0**, which is the last line that supports MongoDB Server **4.2**.

| Tharga.MongoDB | MongoDB.Driver | Lowest MongoDB Server |
|---|---|---|
| 2.16.0 and later | 3.9.0 | **4.2** |
| 2.14.0 - 2.15.1 | 3.10.0 - 3.11.0 | 4.4 |
| 2.0.4 - 2.13.0 | 3.2.1 - 3.9.0 | 4.2 |
| 1.14.6 and earlier | 2.x | 3.6 |

Driver 3.10.0 marked server 4.2 end-of-life and raised `minWireVersion` to 9 (server 4.4). A 3.10+ driver against a 4.2 server fails at the connection handshake, before any query runs — there is nothing a caller can do about it.

## Azure Cosmos DB for MongoDB

Cosmos DB is an emulation of the MongoDB wire protocol rather than MongoDB itself, and the two flavours behave differently:

- **RU (request unit) accounts** report a server version you choose — 4.2 is common, and an account created years ago may still be on it. This is the case the 3.9.0 hold exists for.
- **vCore clusters** run real MongoDB semantics and report 5.0 or later.

### Check which one you have, and what version

From any Mongo client:

```
db.runCommand({ buildInfo: 1 })
```

Or from the Azure CLI — an RU account:

```
az cosmosdb show -g <rg> -n <account> --query "apiProperties.serverVersion" -o tsv
```

A vCore cluster is a different resource type, so the command differs:

```
az cosmosdb mongocluster show -g <rg> -n <cluster> --query "properties.serverVersion" -o tsv
```

If the first command reports "not found" for a resource that exists, you are on vCore.

### Known Cosmos quirks

- **Retryable writes are not supported on RU accounts.** Append `&retrywrites=false` to the connection string, or writes fail with an unsupported-command error.
- **`collStats` must be spelled in camelCase.** Cosmos dispatches the command name case-insensitively but then requires the element spelled exactly, so the lowercase form a real server accepts fails there. `MongoDbService.BuildCollStatsCommand` already handles this.
- **Several aggregation stages and index types are missing on 4.2** — `$lookup`, `$bucket`, `$collStats`, `$indexStats`, and text, 2d, hashed, case-insensitive and sparse indexes among them. See [Microsoft's 4.2 feature support list](https://learn.microsoft.com/en-us/azure/cosmos-db/mongodb/feature-support-42).

## Using a newer driver anyway

The dependency is a **floor**, not a lock — the package declares `MongoDB.Driver >= 3.9.0`, so NuGet resolves 3.9.0 by default. If your servers are all 4.4 or later and you want a newer driver, reference it directly in your own project:

```xml
<PackageReference Include="MongoDB.Driver" Version="3.11.0" />
```

A direct reference wins over the transitive one, and no downgrade warning is raised because you are moving up rather than down.

## Lifting the hold

The hold is tracked in [issue #158](https://github.com/Tharga/MongoDB/issues/158). It is lifted when no consuming environment is on Cosmos 4.2 — either upgraded in place (RU accounts move 4.2 -> 5.0 -> 6.0/7.0 one step at a time, and the upgrade is one-way) or migrated to vCore.
