# Tharga MongoDB
[![NuGet](https://img.shields.io/nuget/v/Tharga.MongoDB)](https://www.nuget.org/packages/Tharga.MongoDB)
![Nuget](https://img.shields.io/nuget/dt/Tharga.MongoDB)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![GitHub repo Issues](https://img.shields.io/github/issues/Tharga/MongoDB?style=flat&logo=github&logoColor=red&label=Issues)](https://github.com/Tharga/MongoDB/issues?q=is%3Aopen)

## Get started
Install the nuget package `Tharga.MongoDB`. It is available at [nuget.org](#https://www.nuget.org/packages/Tharga.MongoDB).

Add *MongoDB* usage to services.
```csharp
builder.AddMongoDB();
```

Or, if you only have access to `IServiceCollection`:
```csharp
builder.Services.AddMongoDB();
```

Add configuration to *appsettings.json*.
```
"ConnectionStrings": {
  "Default": "mongodb://localhost:27017/HostSample{Environment}{Part}"
},
```
Create your entity, repository and collection.
```
public record WeatherForecast : EntityBase
{
    public DateOnly Date { get; set; }
    public int TemperatureC { get; set; }
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
    public string? Summary { get; set; }
}

public interface IWeatherForecastRepository : IRepository
{
    IAsyncEnumerable<WeatherForecast> GetAsync();
    Task AddRangeAsync(WeatherForecast[] weatherForecasts);
}

internal class WeatherForecastRepository : IWeatherForecastRepository
{
    private readonly IWeatherForecastRepositoryCollection _collection;

    public WeatherForecastRepository(IWeatherForecastRepositoryCollection collection)
    {
        _collection = collection;
    }

    public IAsyncEnumerable<WeatherForecast> GetAsync()
    {
        return _collection.GetAsync();
    }

    public async Task AddRangeAsync(WeatherForecast[] weatherForecasts)
    {
        foreach (var weatherForecast in weatherForecasts)
        {
            await _collection.AddAsync(weatherForecast);
        }
    }
}

public interface IWeatherForecastRepositoryCollection : IDiskRepositoryCollection<WeatherForecast>
{
}

internal class WeatherForecastRepositoryCollection : DiskRepositoryCollectionBase<WeatherForecast>, IWeatherForecastRepositoryCollection
{
    public WeatherForecastRepositoryCollection(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<RepositoryCollectionBase<WeatherForecast, ObjectId>> logger)
        : base(mongoDbServiceFactory, logger)
    {
    }
}
```

### Repositories and collections
The framework is based on *repositories* and *collections* and the *entity* to be saved.
- Repositories implements *IRepository*
- Collections implements *IRepositoryCollection*
- Entities implements *IEntity&lt;TKey&gt;*

The repositories and collections are registered in the IOC automatically.

#### Auto-registration scope
By default, `AddMongoDB()` scans assemblies whose name starts with the same prefix as your entry-point assembly (via `AssemblyService.GetAssemblies()`).
This means repositories and collections defined in **external NuGet packages** (e.g. `Tharga.Team.Service`) are **not** discovered automatically.

To include an external assembly, call `AddAutoRegistrationAssembly()`:
```csharp
builder.Services.AddMongoDB(o =>
{
    o.AddAutoRegistrationAssembly(typeof(SomeTypeFromPackage).Assembly);
});
```

You can also replace the default scan entirely:
```csharp
builder.Services.AddMongoDB(o =>
{
    o.AutoRegistrationAssemblies = AssemblyService.GetAssemblies<Program>();
    o.AddAutoRegistrationAssembly(typeof(SomeTypeFromPackage).Assembly);
});
```

**For NuGet package authors:** if your package ships MongoDB collections, either document that consumers must call `AddAutoRegistrationAssembly()`, or handle it inside your own registration method (see `Tharga.Cache.MongoDB` for an example):
```csharp
public static void AddMyFeature(this DatabaseOptions options)
{
    options.AddAutoRegistrationAssembly(Assembly.GetAssembly(typeof(MyMarkerType)));
}
```

#### Collections that take `DatabaseContext`
Auto-registration treats `DatabaseContext` as a runtime parameter. The behavior depends on which constructors a collection exposes:

| Constructor shape | Auto-registered in DI? | How to resolve |
|---|---|---|
| At least one constructor **without** `DatabaseContext` | ✅ Yes | Inject the interface directly, or resolve via `ICollectionProvider` |
| **All** constructors require `DatabaseContext` | ❌ No (by design) | Resolve via `ICollectionProvider.GetCollection<T>(databaseContext)` |

A collection that should support both patterns — direct injection (default context) and per-tenant resolution — must make the `DatabaseContext` parameter optional:
```csharp
public class WeatherRepositoryCollection : DiskRepositoryCollectionBase<WeatherEntity>, IWeatherRepositoryCollection
{
    public WeatherRepositoryCollection(IMongoDbServiceFactory factory, ILogger<WeatherRepositoryCollection> logger,
        DatabaseContext databaseContext = null)        // <-- optional
        : base(factory, logger, databaseContext)
    {
    }
}
```

**Multi-tenant collections** that should always be resolved per-context (i.e. only have constructors that require `DatabaseContext`) are deliberately skipped during auto-registration. Use `ICollectionProvider`:
```csharp
public class WeatherService(ICollectionProvider provider)
{
    public async Task<int> CountAsync(string tenantId)
    {
        var collection = provider.GetCollection<IWeatherRepositoryCollection>(
            new DatabaseContext { DatabasePart = tenantId });
        return (int)await collection.CountAsync();
    }
}
```

**Do not register these collections manually with `AddTransient`** — `DatabaseContext` is not in DI, so resolution fails at startup.

The pattern is built up like this.
The *repository* holds the *collection* inside.
The *repository* exposes the functions, that you create, protecting any operation to be used directly.
The *collection* can be of different types that acts in different ways, it can also be dynamic for *multi tennant* systems.

![Collections](Resources/Repository.png)

### More about collections
There are three implemented types of collections, *IDiskRepositoryCollection* and *ILockableRepositoryCollection* that can be used in different types of scenarios.

#### IDiskRepositoryCollection
This is the main type of collection. It does what you expect, saving and loading data directly from the database.

#### ILockableRepositoryCollection
This is a write-protected collection that you can only update by requesting an exclusive lock.
It can be used similar to a queue.

##### Pick-style lock (decision known up front)
When you already know whether you'll update or delete the document at lock time:

```csharp
await using var scope = await collection.PickForUpdateAsync(id);
scope.Entity.Data = "updated";
await scope.CommitAsync();        // writes the change and clears the lock

await using var del = await collection.PickForDeleteAsync(id);
await del.CommitAsync();          // deletes the document
```

Both methods accept `id`, `FilterDefinition<TEntity>`, or `Expression<Func<TEntity, bool>>` predicate, plus an optional `timeout`, `actor`, and `completeAction` callback. Disposal without `CommitAsync` releases the lock unchanged.

##### Unified lock (decision at commit time)
When you need to inspect the document before deciding update vs delete, use `LockAsync` and pass a `CommitMode` to `CommitAsync`:

```csharp
await using var scope = await collection.LockAsync(id);
if (ShouldDelete(scope.Entity))
{
    await scope.CommitAsync(CommitMode.Delete);
}
else
{
    var updated = scope.Entity with { Data = "updated" };
    await scope.CommitAsync(CommitMode.Update, updated);
}
```

`AbandonAsync` releases without changes; `SetErrorStateAsync(ex)` records an exception on the lock; disposal without commit calls `AbandonAsync`. Same semantics as `PickFor*` — both go through the same internal acquire-lock primitive.

##### Extending a lock ("buy more time")
A long-running job can take a short lock and keep it alive while it works, instead of guessing a large timeout up front. `ExtendLockAsync(TimeSpan extension)` (on the `EntityScope` from `PickFor*`/`WaitFor*` and the `LockScope` from `LockAsync`) pushes the lock's expiry to `UtcNow + extension`:

```csharp
await using var scope = await collection.PickForUpdateAsync(id, timeout: TimeSpan.FromMinutes(5));
foreach (var step in irregularWork)
{
    await DoStepAsync(step);                                  // may take seconds or minutes
    var result = await scope.ExtendLockAsync(TimeSpan.FromMinutes(5));
    // result.ExpireTime  -> the lock's current expiry
    // result.Extended    -> true if this call actually wrote to the database
}
await scope.CommitAsync(scope.Entity with { Data = "done" });
```

It is safe to call on every iteration. To protect the database from a tight or irregular loop, an actual write happens **at most once per `MinLockExtendInterval`** (a `protected virtual` on the collection, default **60 seconds**); calls inside that window are in-memory no-ops that return `Extended = false` with the existing expiry. The first call at or after the window writes immediately — so an irregular job whose step durations are unpredictable always gets its extension through. Pass `force: true` to bypass the throttle for a guaranteed write.

The write is a single atomic, `LockKey`-guarded update. An **expired** lock can still be extended when no other writer has taken it (the `LockKey` still matches), provided delayed operations are allowed — this follows the same `AllowDelayedCommit` gate as commit (strict-TTL collections throw `LockExpiredException` on an expired lock). If the lock was re-acquired by another actor, released, or the document was removed, `ExtendLockAsync` throws `LockExpiredException`; after the scope is committed or abandoned it throws `LockAlreadyReleasedException`.

> Tip: extend by comfortably more than `MinLockExtendInterval` (e.g. extend 5 min with the default 60 s throttle) so the throttle can coalesce writes while keeping plenty of headroom before expiry.

##### Multi-document lease
To inspect several documents and decide each one's fate before committing them all, use `LockManyAsync`. Acquisition is sequential, ordered by key (so two leases targeting overlapping sets always lock in the same order — no AB / BA deadlocks). If any acquisition fails, partial locks are rolled back.

```csharp
await using var lease = await collection.LockManyAsync(new[] { id1, id2, id3 });

foreach (var doc in lease.Documents)
{
    if (ShouldDelete(doc))
        lease.MarkForDelete(doc.Id);
    else if (HasChanges(doc))
        lease.MarkForUpdate(doc with { Data = "..." });
    // else: leave unmarked — released unchanged at commit
}

var summary = await lease.CommitAsync();
// summary.Updated / Deleted / ReleasedUnchanged / Failures
```

Multi-doc commit is **sequential** by default: each decision is applied in mark order, and per-decision failures are collected into `summary.Failures` rather than thrown. For all-or-nothing semantics — atomic apply of every staged decision in one transaction — pass `transactional: true`:

```csharp
var summary = await lease.CommitAsync(transactional: true);
// All decisions land or none do. A single failed decision aborts the transaction.
```

`LockManyAsync` also accepts a `FilterDefinition<TEntity>` or an `Expression<Func<TEntity, bool>>` predicate — both resolve to an id list at acquire time. Transactional commit requires a replica set / sharded MongoDB cluster (see Transactions below).

##### Structured error handling with `ExecuteAsync`

The `EntityScope.ExecuteAsync` extension wraps the common pick → mutate → commit flow into a single call:

```csharp
await using var scope = await collection.PickForUpdateAsync(id);
await scope.ExecuteAsync(async job =>
{
    job.Data = "updated";
    return job;             // commit
    // return null;         // abandon
});
```

Errors thrown inside the func record an exception on the lock (`SetErrorStateAsync`); errors during commit (`LockExpiredException`, `LockAlreadyReleasedException`, `CommitException`, transient I/O) propagate. To consolidate every failure path into one callback, pass an `Action<LockableErrorKind, Exception>` handler:

```csharp
await scope.ExecuteAsync(
    async job => { job.Data = "updated"; return job; },
    (kind, e) => kind switch
    {
        LockableErrorKind.HandlerError       => _logger.LogError(e, e.Message),
        LockableErrorKind.LockExpired        => _logger.LogError(e, "Lock expired for {Job}: {Message}", id, e.Message),
        _                                    => _logger.LogError(e, "Commit error ({Kind}): {Message}", kind, e.Message),
    });
```

`LockableErrorKind` covers `HandlerError` / `LockExpired` / `LockAlreadyReleased` / `CommitError`. The legacy `Action<Exception>` overload is still supported (handles only `HandlerError`; commit errors propagate as before) for callers that haven't migrated.

##### Inspecting and seeding lock state

`entity.GetLockInfo()` returns the current `Lock` (actor, expiry, and any `ExceptionInfo` from a previous failed attempt), or `null` when unlocked. The lock lifecycle is owned by the collection, but the `Lock` type is publicly constructible and can be attached to an entity in memory with `entity.WithLock(...)` — handy for unit-testing lock-reading / error-routing code without a live database:

```csharp
var locked = entity.WithLock(new Lock
{
    LockKey = Guid.NewGuid(),
    LockTime = DateTime.UtcNow,
    ExpireTime = DateTime.UtcNow.AddMinutes(5),
    ExceptionInfo = new ExceptionInfo { Message = "boom" }
});

locked.GetLockInfo().ExceptionInfo.Message.Should().Be("boom");
```

To drive `SetErrorStateAsync` in a test, build a scope over an in-memory entity with `EntityScopeBuilder.Build(entity, releaseAction)` and assert on what the release action receives — no mongod required.

##### Auto-declared lock indexes

Every `LockableRepositoryCollectionBase<TEntity, TKey>` automatically declares two indexes via `CoreIndices` so the lock-check pattern (`{Lock: null}` or `{Lock.ExceptionInfo: null, Lock.ExpireTime: < now}`) doesn't full-scan:

| Name | Keys | Covers |
|---|---|---|
| `Lock` | `{Lock: 1}` | The `Lock == null` branch — unlocked documents. |
| `LockStatus` | `{Lock.ExceptionInfo: 1, Lock.ExpireTime: 1, Lock.LockTime: 1}` | The expired-lock branch + lock-age diagnostics. |

Consumers don't need to add these to their `Indices` override — they're applied alongside any consumer-declared indexes on first collection access (per [Index assurance modes](#index-assurance-modes) above). The merged set is exposed via the `CoreIndices` property if you need to inspect or assert on it from your own tests.

If a consumer's query pattern adds extra fields (e.g. `{Lock: 1, State: 1}` for state-filtered scans), declare those compounds in the consumer's `Indices` — they merge with `CoreIndices` rather than replacing it.

For collections that pre-date the upgrade and are missing these indexes in production, see [Re-applying indexes after a code change](#re-applying-indexes-after-a-code-change) — the `RestoreAllIndicesAsync` API, the *Assure all indices* Blazor toolbar action, and the `mongodb.restore_all_indexes` MCP tool all force a re-apply across tracked collections.

---

### Transactions

Multi-document writes can be wrapped in a MongoDB transaction so they all commit atomically (or all roll back on exception). Works across multiple collections in the same cluster, and supports both Disk and Lockable repositories — including taking and committing locks inside the same transaction.

```csharp
await mongoDbServiceFactory.WithTransactionAsync(async (session, ct) =>
{
    await jobsRepo.AddAsync(job, session);
    await statsRepo.AddAsync(stat, session);

    await using var scope = await accountRepo.LockAsync(accountId, session: session);
    await scope.CommitAsync(CommitMode.Update, scope.Entity with { Balance = newBalance });
});
```

The session is created on the cluster identified by `configurationName` (default if null). The driver retries on transient transaction errors automatically. Body exceptions abort and rethrow.

For the most common case — *lock N docs, decide each, commit atomically* — you don't need to touch `IClientSessionHandle` at all:

```csharp
await using var lease = await coll.LockManyAsync(ids);
// inspect, mark...
var summary = await lease.CommitAsync(transactional: true);
```

Acquisition stays unchanged (sequential, fast); the commit pass runs inside an internal transaction so all marked decisions land atomically.

#### Requirements
- **Replica set or sharded cluster.** MongoDB transactions don't work on standalone deployments; the driver throws on `StartTransaction`.
- All collections inside one transaction must be backed by the same `MongoClient` (same cluster). Cross-cluster transactions aren't supported by MongoDB.
- Default 60-second transaction timeout — don't try to work around it. For long workflows, do the deciding outside the transaction and only wrap the commit pass.

#### Behavior under an active session
- `OperationIndexManagement` is **skipped** when a session is active (Mongo forbids index DDL inside a transaction). Indexes are assured by the next non-transactional call against the collection. If your transaction is the *first* call against a fresh collection on process startup, the index won't be assured until later — warm up the collection at startup.
- `DropEmptyAsync` (auto-drop after the last delete) is similarly skipped under a session.

#### Out of scope
- Cross-cluster transactions
- Nested transactions / savepoints
- Long-running transactions
- Session-aware reads (`GetAsync(filter, session)` etc.) — write atomicity is the focus; filed as a follow-up

### Simpler way of doing repositories
The simplest way is to have the *repository* implement the *collection* directly.
The downside is that you cannot protect access to methods, the cosumer will have access to it all.
```
public class MySimpleRepo : DiskRepositoryCollectionBase<MyEntity>
{
    public MySimpleRepo(IMongoDbServiceFactory mongoDbServiceFactory)
        : base(mongoDbServiceFactory)
    {
    }
}

public record MyEntity : EntityBase
{
}
```

## Simple Console Sample
This is a simple demo for a console application written in .NET 7.
The following nuget packages are used.
- Tharga.MongoDB
- Microsoft.Extensions.Hosting

```
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using Tharga.MongoDB;
using Tharga.MongoDB.Disk;

var services = new ServiceCollection();
services.AddMongoDB(o.ConnectionStringLoader = (_,_) => Task.FromResult<ConnectionString>("mongodb://localhost:27017/SimpleDemo"));

var serviceProvider = services.BuildServiceProvider();

var simpleRepo = serviceProvider.GetService<MySimpleRepo>();
await simpleRepo!.AddAsync(new MyEntity());
var oneItem = await simpleRepo.GetOneAsync(x => true);

Console.WriteLine($"Got item with id '{oneItem.Id}' from the database.");

public class MySimpleRepo : DiskRepositoryCollectionBase<MyEntity, ObjectId>
{
    public MySimpleRepo(IMongoDbServiceFactory mongoDbServiceFactory)
        : base(mongoDbServiceFactory)
    {
    }
}

public record MyEntity : EntityBase<ObjectId>
{
}
```

---

## More details

### Configuration
Configuring can be done in `appsettings.json` or by code. Code is always used first value by value.
If using multiple (named) databases, configuration will always use the named version first if there is one and then use the general fallback value.
This is the order used, value by value.
1. Named configuration from code
1. General configuration from code
1. Named configuration from IConfiguration
1. General configuration from IConfiguration
1. Default values

#### Example of configuration by `appsettings.json`.
When the 'Default' database is used, the result limit will be 100, for the 'Other' database the result limit will be 200.
If another database is implemented, the fallback of 1000 will be used as result limit.

The 'Default' database will have the firewall opened, if hosted in Atlas MongoDB.

```
  "ConnectionStrings": {
    "Default": "mongodb://localhost:27017/Tharga{environment}_Sample{part}",
    "Other": "mongodb://localhost:27017/Tharga{environment}_Sample_Other{part}"
  },
  "MongoDB": {
    "Default": {
      "AccessInfo": {
        "PublicKey": "[PublicKey]",
        "PrivateKey": "[PrivateKey]",
        "GroupId": "[GroupId]"
      },
      "ResultLimit": 100,
      "AutoClean": true,
      "CleanOnStartup": true,
      "CreateCollectionStrategy": "DropEmpty"
    },
    "Other": {
      "ResultLimit": 200
    },
    "ResultLimit": 1000
    "AutoClean": false,
    "CleanOnStartup": false,
    "CreateCollectionStrategy": "DropEmpty"
  }
```

#### Example of configuration by code.
This would be the same configuration as from the example above.
```
services.AddMongoDB(o =>
{
    o.ConnectionStringLoader = async (name, provider) =>
    {
        return (string)name switch
        {
            "Default" => "mongodb://localhost:27017/Tharga{environment}_Sample{part}",
            "Other" => "mongodb://localhost:27017/Tharga{environment}_Sample_Other{part}",
            _ => throw new ArgumentException($"Unknown configuration name '{name}'.")
        };
    };
    o.ConfigurationLoader = async () => new MongoDbConfigurationTree
    {
        Configurations = new Dictionary<ConfigurationName, MongoDbConfiguration>
        {
            {
                "Default", new MongoDbConfiguration
                {
                    AccessInfo = new MongoDbApiAccess
                    {
                        PublicKey = "[PublicKey]",
                        PrivateKey = "[PrivateKey]",
                        GroupId = "[GroupId]"
                    },
                    ResultLimit = 100,
                    AutoClean = true,
                    CleanOnStartup = true,
                    "CreateCollectionStrategy": "DropEmpty"
                }
            },
            {
                "Other", new MongoDbConfiguration
                {
                    ResultLimit = 200
                }
            }
        },
        ResultLimit = 1000,
        AutoClean = false,
        CleanOnStartup = false,
        "CreateCollectionStrategy": "DropEmpty"
    };
});
```

## ConnectionStringLoader
To dynamically use connectionstrings depending on *ConfigurationName* or other parameters it is possible to create a custom implementation of *ConnectionStringLoader*.
If it is not implemented, or returns null, then the configuration in *IConfiguration* will be used.

After the *ConnectionStringLoader* is called the [MongoUrl Builder](#mongourlbuilder) will run. This means you can provide any variables (Values between '\{' and '\}') that your *MongoUrl Builder* can handle

This is the simplest version to be implemented.
```
services.AddMongoDB(o.ConnectionStringLoader = (_,_) => Task.FromResult<ConnectionString>("mongodb://localhost:27017/MyDatabase{part}"));
```

You can also implement your own class for this.
```
public void ConfigureServices(IServiceCollection services)
{
    services.AddTransient<ConnectionStringLoader>();
    services.AddMongoDB(o =>
    {
        o.ConnectionStringLoader = async (name, provider) => await provider.GetService<ConnectionStringLoader>().GetConnectionString(name);
    });
}

public class ConnectionStringLoader
{
    private readonly ISomeDependency _someDependency;

    public ConnectionStringLoader(ISomeDependency someDependency)
    {
        _someDependency = someDependency;
    }

    public async Task<string> GetConnectionString(string configurationName)
    {
        switch (configurationName)
        {
            case "A":
                //Load value from other location
                return await _someDependency.GetValueAsync();
            case "B":
                //Build string dynamically
                return $"mongodb://localhost:27017/Tharga_{Environment.MachineName}{{part}}";
            case "C":
                //Use IConfiguration
                return null;
            default:
                throw new ArgumentOutOfRangeException($"Unknown configurationName '{configurationName}'.");
        }
    }
}
```


### Customize collections
Properties for classes deriving from `RepositoryCollectionBase<,>` can be customised directly by overriding the default behaviour of the code or configuration.

By default the name of the collection is the same as the type name of the entity.
To have a different name the property `CollectionName` can be overridden.

The name of the database can be built up dynamically, use `DatabasePart` to do so.
Read more about this in the section [MongoUrl Builder](#mongourlbuilder).

Override property `ConfigurationName` to use different database than default (or set as default in `DatabaseOptions`).
This makes it possible to use multiple databases from the same application.

The properties `AutoClean`, `CleanOnStartup`, `CreateCollectionStrategy` and `ResultLimit` can be overridden by collection to be different from the configuration.

To automatically register known types when using multiple types in the same collection, provide a value for `Types`.

Create `Indices` by overriding the property in your collection class.
The list of `Indices` is applied befor the first record is added to the collection.
It is also reviewed once every time the application starts, removing `Indices` that no longer exists and creates new ones if the code have changed.

#### Index assurance modes

`AssureIndexMode` (set via `DatabaseOptions.AssureIndexMode` or per configuration) controls how the library reconciles the indexes you declare in code with the indexes that exist in MongoDB. Each mode has different reconciliation semantics — pick one based on how you roll out index changes:

| Mode | Names required | Detects schema change | Comment |
|---|---|---|---|
| `ByName` (default) | yes | no | Fastest. Names must be set on every `CreateIndexOptions`. Indexes are matched by name only — if you change the schema (fields/uniqueness) but keep the name, the change is **not** detected and **not** applied. To roll out a schema change, rename the index. |
| `BySchema` | optional | yes | Names optional. Indexes are matched by their rendered schema (key fields + uniqueness). Schema changes are detected and applied. Renaming an index in code while keeping the same schema does **not** rename the live index — the existing one is treated as up-to-date. |
| `DropCreate` | optional | n/a | Drops every non-`_id` index and recreates them on every assurance pass. Always converges, but expensive — generally only useful in non-production. |
| `Disabled` | n/a | n/a | Skips index assurance entirely. Useful for read-only consumers or one-shot deploy tooling that doesn't own the schema. |

For both `BySchema` and `DropCreate`, declaring two indexes with the same explicit name throws `InvalidOperationException` up front (mirrors `ByName`). `BySchema` additionally logs a warning when two declared indexes have identical schema (typically a copy-paste error — only one ends up in MongoDB).

#### Re-applying indexes after a code change

Index assurance runs lazily — the first access to a collection triggers it. For an already-deployed environment that holds many tenant collections, that means new indexes only land when each collection is next touched. To force a one-shot re-apply across every tracked collection, use:

- **API:** `IDatabaseMonitor.RestoreAllIndicesAsync(filter, progress, cancellationToken)` — iterates `GetInstancesAsync()` and calls `RestoreIndexAsync` per collection. Returns an `IndexAssureSummary` (total / succeeded / failed / skipped). Optional `filter: CollectionInfo => bool` narrows the scope; optional `IProgress<IndexAssureProgress>` reports per-collection outcomes.
- **Blazor toolbar:** click *Assure all indices* in the `MonitorToolbar` component — emits a notification per collection and a final summary.
- **MCP:** call the `mongodb.restore_all_indexes` tool via `Tharga.MongoDB.Mcp`. Optional `configurationName` / `databaseName` arguments narrow the scope.

The helper is **not** auto-run on app startup — keep timing under your own control.

### MongoUrl Builder
The `MongoUrl` is created by a built in implementation of `IMongoUrlBuilder`. It takes the raw version and parses variables to build `MongoUrl`.

Two variables are supported `{environment}` and `{part}`.

To dynamicaly change the name of the database `{part}` can be used. It can be used as an override to a collection or provided as a variable in `DatabaseContext` together with [CollectionProvider](#collectionprovider).

For `{environment}` the value will be ommitted when it is set to 'Production'.

Both variables will get a leading character of '_'.

Example for Development with the databasePart = MyPart.
`mongodb://localhost:27017/Tharga{environment}_Sample{part}` --> `mongodb://localhost:27017/Tharga_Development_Sample_MyPart`

#### Custom MongoUrl Builder
If there is a need for a custom string builder, implement the interface `IMongoUrlBuilder` and register with the IOC and that will be used instead of the built in version.
Register your own version of IMongoUrlBuilder in IOC.
```
services.AddTransient<IMongoUrlBuilder, MyMongoUrlBuilder>();
```

---

## Atlas MongoDB Firewall
When configuring the `AccessInfo` and the database is accessing a database other than localhost the firewall will be opened automatically for the current IP.
There are more details on the [mongodb.com](https://www.mongodb.com/docs/atlas/configure-api-access/#std-label-create-org-api-key) site.

### Public- and PrivateKey
To create a key-pair, select *Access Manager* for the *organization*. Then Select the tab *Applications* and *API Keys*. Here you can create keys with the correct access.

#### GroupId
The *GroupId* can be found as part of the URL on the *Atlas MongoDB* website.
Example. `https://cloud.mongodb.com/v2/[GroupId]`

### Service account (OAuth2)
As an alternative to the API key pair, the Atlas-direct path can authenticate with an [Atlas Service Account](https://www.mongodb.com/docs/atlas/api/service-accounts/) (OAuth2 client credentials). Set `ClientId` and `ClientSecret` on `AccessInfo` instead of `PublicKey`/`PrivateKey` (the service account is used when both are set); Tharga.MongoDB exchanges them for a short-lived bearer token automatically. Works in Classic and Notify mode.

> Service-account secrets **expire** and must be rotated. A failed token exchange raises `AtlasServiceAccountAuthException` (carrying `StatusCode` and a best-effort `LikelyExpired` flag — Atlas returns 401 for both invalid and expired secrets, so it can't be definitive).

### Optional Quilt4Net firewall proxy
For deployments where you don't want individual services to hold an Atlas API key, [Quilt4Net.Server](https://www.nuget.org/packages/Quilt4Net.Toolkit) can act as a central firewall manager — it holds the Atlas Project-Owner credential and exposes a proxy API that opens the firewall on behalf of consumers, then auto-closes openings that stop being used. Tharga.MongoDB integrates with that proxy via two optional fields on `MongoDbApiAccess`:

```csharp
o.AccessInfo = new MongoDbApiAccess
{
    GroupId          = "<atlas-project-group-id>",
    Quilt4NetBaseUrl = "https://your-quilt4net.example.com/", // defaults to https://quilt4net.com/
    Quilt4NetApiKey  = "<your-quilt4net-firewall-key>",
    // PublicKey / PrivateKey optional — see modes below.
};
```

The mode is **inferred** from which keys you populate:

| Atlas credential\* | Quilt4Net key | Mode | Behaviour |
|:--:|:--:|:--|:--|
| ✔ | ✘ | **Classic** | Direct Atlas open. Today's behaviour, unchanged. |
| ✔ | ✔ | **Notify** | Direct Atlas open + periodic Quilt4Net `ReportUsedAsync` heartbeat so the central system knows this IP is in use. |
| ✘ | ✔ | **Open** | Quilt4Net opens the firewall via its proxy. Subsequent heartbeats reuse the same `OpenAsync` call (returns `AlreadyOpen` once the firewall is open, which serves as the usage signal — no separate `ReportUsedAsync` needed). The consumer never holds an Atlas credential. |
| ✘ | ✘ | None | No firewall management. |

\* *Atlas credential* = a Programmatic API key pair (`PublicKey`+`PrivateKey`) **or** a Service Account (`ClientId`+`ClientSecret`); either drives the Atlas-direct open.

The heartbeat is driven by a background service registered automatically by `AddMongoDB`. Set `DatabaseOptions.Quilt4NetHeartbeatInterval` to tune the cadence (default 5 minutes) or `null` to disable. The service is dormant when no access is in Notify/Open mode, so consumers without `Quilt4NetApiKey` pay nothing at runtime.

401/403 from the proxy surfaces as `Quilt4NetFirewallAuthorizationException` and the affected entry is dropped from the heartbeat loop — a misconfigured key won't burn cycles retrying. Transient HTTP failures keep the entry so the next tick retries.

## Resilient startup connectivity

When a configured connection is unreachable at startup (e.g. the egress IP is not yet in the Atlas access list), `UseMongoDB` runs a **connectivity pre-check** — one non-throwing probe per configured connection, built on the same check as `IMongoDbService.GetInfoAsync()` / `DatabaseInfo.CanConnect`. Unreachable connections are retried with exponential backoff.

If any connection is still unreachable after the retries, the failure is **logged (`LogCritical`) and the `StartupFailureCallback` is awaited** (so telemetry can be flushed) before the configured reaction takes effect:

- **`FailFast`** (default) — a `MongoStartupConnectivityException` is thrown. The process still exits as before, but the failure is now observable in your telemetry backend instead of an unhandled, untelemetered abort.
- **`Degrade`** — the host starts anyway. `IMongoDbConnectivityState` reports the connection as unhealthy (and a registered health check reports unhealthy) until connectivity is restored, while the rest of the app keeps running and telemetry keeps flowing.

The pre-check is skipped when `DatabaseOptions.ReadyCallback` is configured (connection strings arrive later), mirroring the firewall-open skip.

```csharp
app.UseMongoDB(o =>
{
    o.StartupConnectivity = StartupConnectivityMode.Degrade;     // default is FailFast
    o.StartupConnectivityRetryCount = 3;                          // attempts per connection
    o.StartupConnectivityRetryDelay = TimeSpan.FromSeconds(2);    // initial backoff, doubled per retry

    // Awaited before rethrow (FailFast) or continue (Degrade). Flush your telemetry here so the
    // failure reaches Application Insights / OpenTelemetry even on a fail-fast exit.
    o.StartupFailureCallback = async (failure, sp) =>
    {
        // Application Insights:
        sp.GetService<TelemetryClient>()?.Flush();
        await Task.Delay(TimeSpan.FromSeconds(5));   // give the async channel time to drain
        // OpenTelemetry:
        // sp.GetService<TracerProvider>()?.ForceFlush();
    };
});
```

> The library never references App Insights / OTel itself — the `StartupFailureCallback` is where you flush your own pipeline. It also logs `LogCritical` regardless, so the failure reaches any `ILogger` provider you have wired.

### Health / readiness endpoint

`IMongoDbConnectivityState` (registered by `AddMongoDB`) exposes per-connection reachability and an aggregate `IsHealthy`, and re-evaluates live via `CheckAsync()`. Wire it into ASP.NET Core health checks with the opt-in helper:

```csharp
builder.Services.AddHealthChecks()
    .AddMongoDb(name: "mongodb", tags: ["ready"]);   // live = true re-probes per call and auto-recovers
```

Or read it directly:

```csharp
var state = app.Services.GetRequiredService<IMongoDbConnectivityState>();
if (!state.IsHealthy)
    foreach (var c in state.Connections.Where(x => !x.CanConnect))
        logger.LogWarning("MongoDB connection {Config} is down: {Message}", c.ConfigurationName, c.Message);
```

The `_monitor` cache load and its "drop and start fresh" recovery are also hardened — an unreachable server there is logged and skipped (the monitor starts with an empty cache and repopulates on first access) rather than aborting the process.

## Tracking external collections

When an external NuGet package registers its own collection types via DI (e.g. `services.AddTransient<IMyCollection, MyCollection>()`), the database monitor may show them as "NotInCode" because they were not discovered by the auto-registration scan.

Use `TrackMongoCollection` to tell the monitor about them without duplicating the DI registration:

```csharp
// Non-generic — useful when types are constructed at runtime via MakeGenericType()
services.TrackMongoCollection(typeof(IMyCollection), typeof(MyCollection));

// Generic — when types are known at compile time
services.TrackMongoCollection<IMyCollection, MyCollection>();
```

This only affects monitor visibility — it does **not** register the type in DI. The call can be placed before or after `AddMongoDB`; the actual merge happens in `UseMongoDB`.

This is intended for library authors whose collection implementation types are `internal`. Consumers of the library don't need to do anything.

## Collection interceptors

A pre-call hook that runs before a repository operation reaches the MongoDB driver, and can reject it. Register one and no database call can happen without your own policy layer having run first.

```csharp
public sealed class TeamAccessInterceptor : ICollectionInterceptor
{
    public ValueTask<InterceptDecision> BeforeCallAsync(CollectionCallInfo call, CancellationToken cancellationToken = default)
    {
        if (TeamAccess.Current is null)
            return ValueTask.FromResult(InterceptDecision.Reject($"No team scope for {call.CollectionName}."));

        return ValueTask.FromResult(InterceptDecision.Proceed);
    }
}
```

```csharp
builder.AddMongoDB(o => o.AddCollectionInterceptor<TeamAccessInterceptor>());
```

Interceptors are resolved from DI and run in registration order; the first rejection short-circuits the rest and throws `CollectionAccessDeniedException`, carrying the reason and the `CollectionCallInfo`. Throwing your own exception also blocks the call and propagates unchanged.

The package stays mechanism-only — it knows nothing about teams, tenancy or authorization, and simply runs what is registered.

### Coverage

Every public data operation is intercepted, on both disk and lockable collections, however the collection was obtained — via `ICollectionProvider`, via DI, or constructed directly with an `IMongoDbServiceFactory`. That last route is why decorating `ICollectionProvider` is not equivalent: a collection that takes the factory in its constructor never goes through the provider. For lockable collections the lock lifecycle (acquire, extend, commit, release) is covered too, since each is a write routed through the same disk operations.

Not intercepted: the internal index and maintenance plumbing (index assurance and drop, collection clean, monitor metadata reads). These are internal to the package and driven by the monitor's admin surface.

### Timing points

An interceptor declares which point(s) it wants via `Points`, defaulting to `InterceptionPoint.Invocation` — the point at which the caller made the request, while its ambient context is still in scope. This is what a policy gate wants, and it is the only meaningful point for operations returning `IAsyncEnumerable`, whose database work would otherwise happen at enumeration time.

`InterceptionPoint.Enumeration` fires inside the iterator at cursor open, for concerns that must affect the observed timing of a deferred result. An interceptor may declare both and tell the calls apart by `CollectionCallInfo.Point`.

### Notes

- Key on `CollectionCallInfo.OperationType` rather than the `Operation` string to distinguish reads from writes — a lockable `PickForUpdateAsync` reports as the `UpdateOneAsync` that actually runs.
- A synchronous interceptor rejects at the call site, even for streaming operations. One that genuinely yields surfaces its rejection on first enumeration. Either way nothing reaches the driver.
- With no interceptors registered the path is a field read and a branch — no allocation, and no `CollectionCallInfo` construction.
- Interceptors are effectively singletons; keep per-operation state in an `AsyncLocal`, not in the interceptor. Do not call back into a repository collection from one.
- Rejected calls are invisible to the monitor — they never touched the database. Audit them in your interceptor.

This is the veto-capable counterpart to the static, observational `RepositoryCollectionBase.ActionEvent`, which is unchanged. Use `ActionEvent` to watch; use an interceptor to decide.

See the [collection interceptors docs](https://github.com/Tharga/MongoDB/blob/master/docs/articles/collection-interceptors.md) for the full contract, timing-point semantics and caveats.

## Monitor
The built-in monitor tracks collection metadata such as document counts, sizes, indexes and clean status.
By default the monitor persists its state to a `_monitor` collection in MongoDB so that data survives application restarts and is shared across instances.

### Storage mode
Set `StorageMode` to control where the monitor keeps its state.

| Mode | Behaviour |
|---|---|
| `Database` (default) | Persists to the `_monitor` collection. State survives restarts. |
| `Memory` | In-memory only. State is lost on restart. |

#### Configuration by `appsettings.json`
```json
"MongoDB": {
  "Monitor": {
    "Enabled": true,
    "StorageMode": "Database",
    "LastCallsToKeep": 1000,
    "SlowCallsToKeep": 200,
    "ForwardCompletedCalls": false,
    "QueueMetricInterval": "00:00:01",
    "ClusterConnectionLimit": 3000
  }
}
```

`ForwardCompletedCalls` (default `false`) and `QueueMetricInterval` apply to agents forwarding to a central monitor; `ClusterConnectionLimit` is read by the central server to show open connections against a cluster's limit (one value for every cluster). For mixed deployments — different Atlas tiers, or self-hosted — set `ClusterConnectionLimitResolver` in code (below) to resolve the limit per cluster instead. See [Centralised monitoring](#centralised-monitoring) and the [monitoring docs](https://github.com/Tharga/MongoDB/blob/master/docs/articles/monitoring.md).

#### Configuration by code
```csharp
services.AddMongoDB(o =>
{
    o.Monitor = new MonitorOptions
    {
        Enabled = true,
        StorageMode = MonitorStorageMode.Database,
        LastCallsToKeep = 1000,
        SlowCallsToKeep = 200,
        ForwardCompletedCalls = false,        // opt-in: forward every completed call to the central monitor
        QueueMetricInterval = TimeSpan.FromSeconds(1),
        // How much per-call data to record. OnDemand (default) keeps the lightweight call always but builds the
        // step timeline only while consumed (forwarding on or a live viewer). WhenConsumed records nothing while
        // idle — best for a headless agent. Full always records everything.
        CallRecordingLevel = CallRecordingLevel.OnDemand,
        // Capture driver command durations (the "Driver: … | Other: …" step breakdown). This is the startup
        // default; the listener is always subscribed, so it can be toggled at runtime — locally via
        // IDatabaseMonitor.SetCommandMonitoring, or per-agent from the Clients dialog (SetClientCommandMonitoringAsync).
        EnableCommandMonitoring = false,
        // Per-cluster connection limit (drives the "open / limit" bar). Resolve per cluster so mixed
        // deployments — different Atlas tiers, or self-hosted — each show the right ceiling, or none:
        ClusterConnectionLimitResolver = (sp, ctx) => ctx.IsAtlas ? 3000 : (int?)null
        // ClusterConnectionLimit = 3000      // alternative: one limit for every cluster
    };
});
```

### Source identification
All monitoring data is tagged with a source name to identify where it originates from. This is useful when multiple applications share the same database or when preparing for distributed monitoring.

By default the source name is `{MachineName}/{AssemblyName}`. To override:

```json
"MongoDB": {
  "Monitor": {
    "SourceName": "OrderService-Prod"
  }
}
```

Or by code:
```csharp
services.AddMongoDB(o =>
{
    o.Monitor.SourceName = "OrderService-Prod";
});
```

The Blazor call view automatically shows a Source column and filter when calls from multiple sources are present.

### Command monitoring
Enable driver-level command monitoring to see how much of the "Action" step is actual MongoDB server time vs thread pool wait. Disabled by default.

```json
"MongoDB": {
  "Monitor": {
    "EnableCommandMonitoring": true
  }
}
```

When enabled, steps that involve MongoDB driver calls include a breakdown of driver time vs other overhead (serialization, thread pool wait, etc.):

- **FetchCollection**: `Driver: listIndexes 2.10ms | Other: 0.45ms`
- **OperationIndexManagement**: `Driver (2): createIndexes 8.50ms, listIndexes 1.20ms | Other: 0.30ms`
- **Action**: `Driver: find 12.34ms | Other: 3.21ms`

This helps diagnose whether slow operations are caused by the database, serialization, or application-side contention.

### Remote forwarding
Install the `Tharga.MongoDB.Monitor.Client` package to forward monitoring data from a remote agent to a central server via [Tharga.Communication](https://www.nuget.org/packages/Tharga.Communication).

```csharp
builder.AddMongoDB();
builder.AddMongoDbMonitorClient(sendTo: "https://monitor-server");
```

For hosts that only expose `IServiceCollection` at registration time — for example a `Tharga.Wpf` agent whose `App.Register(HostBuilderContext context, IServiceCollection services)` callback has no builder in scope — use the `IServiceCollection` overload:

```csharp
services.AddMongoDbMonitorClient(
    configuration: context.Configuration,
    sendTo: "https://monitor-server");
```

Both overloads behave identically once `sendTo` is set.

The forwarder subscribes to call events and sends `CallDto` via fire-and-forget. When the server is unavailable or not configured, there is zero overhead. The hub endpoint defaults to `/mongodb-monitor`.

### Receiving remote monitoring data
Install the `Tharga.MongoDB.Monitor.Server` package on the central server (typically the Blazor dashboard app) to receive monitoring data from remote agents.

```csharp
builder.AddMongoDB();
builder.AddMongoDbMonitorServer();

app.UseMongoDB();
app.UseMongoDbMonitorServer();
```

The hub is mapped at `/mongodb-monitor` by default. Both client and server accept an optional pattern override if needed.

Remote calls are ingested into the local `IDatabaseMonitor` and appear automatically in Blazor components, REST API endpoints, and summaries alongside local data. The Source column and filter appear when calls from multiple sources are present.

The Clients page shows, per agent, both the host application **Version** (from the Tharga.Communication handshake) and the **Library** version of `Tharga.MongoDB.Monitor.Client` it runs; the dashboard's own `Tharga.MongoDB.Monitor.Server` version is shown above the grid.

### Securing the monitor hub
Both client and server support API key authentication via [Tharga.Communication](https://www.nuget.org/packages/Tharga.Communication). When keys are configured, unauthorized agents are rejected. When no keys are configured, all connections are accepted (backwards compatible).

```csharp
// Agent
builder.AddMongoDbMonitorClient(sendTo: "https://monitor-server", apiKey: "my-secret-key");

// Server
builder.AddMongoDbMonitorServer(primaryApiKey: "my-secret-key");
```

For zero-downtime key rotation, configure both primary and secondary keys on the server — either key is accepted:

```csharp
builder.AddMongoDbMonitorServer(primaryApiKey: "new-key", secondaryApiKey: "old-key");
```

API keys can also be provided via `appsettings.json` or [User Secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets) (recommended for development):

**Agent** (`appsettings.json` or User Secrets):
```json
{
  "Tharga": {
    "Communication": {
      "ApiKey": "my-secret-key"
    }
  }
}
```

**Server** (`appsettings.json` or User Secrets):
```json
{
  "Tharga": {
    "Communication": {
      "PrimaryApiKey": "my-secret-key",
      "SecondaryApiKey": "old-key-during-rotation"
    }
  }
}
```

To use User Secrets in development (keys stay out of source control):
```bash
# Agent
dotnet user-secrets set "Tharga:Communication:ApiKey" "my-secret-key"

# Server
dotnet user-secrets set "Tharga:Communication:PrimaryApiKey" "my-secret-key"
```

### Remote action delegation
When the server dashboard displays collections from remote agents, actions like Touch, Drop Index, Restore Index, and Clean are automatically delegated to the connected agent that owns the collection. No additional configuration is needed — if the `Tharga.MongoDB.Monitor.Client` and `Tharga.MongoDB.Monitor.Server` packages are installed, delegation works out of the box.

- **Local collections**: actions execute directly on the server (existing behavior)
- **Remote-only collections**: actions are forwarded to the connected agent via `IServerCommunication.SendMessageAsync`
- **No agent connected**: an error is returned to the UI

### Per-agent detail dialog
On the **Clients** tab, clicking a row opens a per-agent dialog showing what that agent has contributed: collections it has reported, the most recent calls (filtered by source), and the latest queue snapshot. Useful for triaging "is agent X reporting and what is it sending right now" in deployments with multiple agents. Powered by `IDatabaseMonitor.GetMonitorClientDetail(sourceName, recentCallLimit = 20)` so the same data is available to non-Blazor consumers.

### Subscription-based live monitoring
Live monitoring data (queue metrics, ongoing calls) is only sent by remote agents when someone is actively viewing the Queue or Ongoing tab. This is automatic — Blazor components subscribe on mount and unsubscribe when the tab closes. Collection metadata and completed calls are always sent regardless of subscriptions.

### Reset
Call `IDatabaseMonitor.ResetAsync()` to clear all cached monitor state (both in-memory and persisted).
The Blazor admin UI (`CollectionView`) includes a Reset button that triggers this.

### CollectionView at scale
At thousands of collections + a busy multi-agent call firehose the **Collections** tab uses a per-process stale-while-revalidate cache so navigation back to it renders synchronously. First navigation per host process still loads from MongoDB (one user pays); every subsequent navigation across all admin users on that host is instant.

After the synchronous render, a background revalidator (`RevalidationQueue`) refreshes each row from MongoDB with a 16-way concurrency cap so the database doesn't see thousands of simultaneous fetches. The currently-visible page is fetched first; off-screen rows fill in afterwards. Each row that's being refreshed is highlighted in blue (Radzen `--rz-info-light`) so it's clear which values are stale-but-loading. Hourly cell color codes:

- **Yellow** — value not yet known (stats never loaded for this collection).
- **Pink** — index definitions don't match the registered schema.
- **Blue** — background revalidation in flight; the value shown is from cache.

The revalidation refreshes `Stats`, `Discovery`, `Indices`, and `Clean`. `CallCount` and `Sources` follow their own event-driven paths and update independently. The cache is in-memory only — it does not survive a host restart, and load-balanced instances each pay the first-load cost.

### REST API integration
The monitor exposes API-friendly methods that return JSON-serializable DTOs.
Wire them to your endpoints with minimal code:

```csharp
// Slow calls with timing, filter, and step breakdown
app.MapGet("/api/monitor/slow-calls", (IDatabaseMonitor m) => m.GetCallDtos(CallType.Slow));

// Recent calls
app.MapGet("/api/monitor/recent-calls", (IDatabaseMonitor m) => m.GetCallDtos(CallType.Last));

// Call summary grouped by collection+function (find chatty or slow patterns)
app.MapGet("/api/monitor/call-summary", (IDatabaseMonitor m) => m.GetCallSummary());

// Error summary grouped by exception type and collection
app.MapGet("/api/monitor/errors", (IDatabaseMonitor m) => m.GetErrorSummary());

// Slow calls with index coverage info (find missing indices)
app.MapGet("/api/monitor/slow-calls-index", async (IDatabaseMonitor m) =>
    await m.GetSlowCallsWithIndexInfoAsync().ToListAsync());

// Explain plan for a specific call
app.MapGet("/api/monitor/explain/{callKey}", (IDatabaseMonitor m, Guid callKey) =>
    m.GetExplainAsync(callKey));

// Call counts per collection
app.MapGet("/api/monitor/call-counts", (IDatabaseMonitor m) => m.GetCallCounts());

// Connection pool state (queue depth, executing count, wait time)
app.MapGet("/api/monitor/pool", (IDatabaseMonitor m) => m.GetConnectionPoolState());
```

| Method | Returns | Use case |
|---|---|---|
| `GetCallDtos(CallType)` | `CallDto[]` | Serializable call data with filter, steps, timing |
| `GetExplainAsync(Guid)` | `string` | MongoDB explain plan for a specific call |
| `GetCallCounts()` | `Dictionary<string, int>` | Call frequency per collection |
| `GetCallSummary()` | `CallSummaryDto[]` | Grouped by collection+function: count, avg/max/min elapsed |
| `GetErrorSummary()` | `ErrorSummaryDto[]` | Errors grouped by type and collection |
| `GetSlowCallsWithIndexInfoAsync()` | `SlowCallWithIndexInfoDto[]` | Slow calls with index coverage analysis |
| `GetConnectionPoolState()` | `ConnectionPoolStateDto` | Aggregate queue depth, executing count, wait time, recent metrics |
| `GetPerPoolQueueState()` | `IReadOnlyDictionary<string, ConnectionPoolStateDto>` | Queue/exec per connection pool (per cluster), across this process and all agents — one entry per source+pool, labelled by configuration |
| `GetInFlightCalls()` | `InFlightCallInfo[]` | What the limiter is holding right now (queued vs executing) — for diagnosing a flood |
| `GetClusterConnectionSummary()` | `ClusterConnectionSummary[]` | Open connections + capacity per cluster across all sources, vs the configured `ClusterConnectionLimit`. Three-level breakdown: cluster (host) → pool (server-key) → source (process) |

---

## MCP (Model Context Protocol)

The `Tharga.MongoDB.Mcp` package exposes MongoDB monitoring data and admin actions via MCP, so AI agents can query collections, inspect monitoring data, and trigger actions.

Install `Tharga.MongoDB.Mcp` and register inside the `AddThargaMcp` callback:

```csharp
services.AddThargaMcp(mcp =>
{
    mcp.AddMongoDB();
});

app.UseThargaMcp();
```

### Data access levels

By default, `Tharga.MongoDB.Mcp` exposes only metadata and admin tools — nothing that returns or modifies actual document data. To expose more, opt in:

```csharp
services.AddThargaMcp(mcp =>
{
    mcp.AddMongoDB(o =>
    {
        // Default: DataAccessLevel.Metadata
        o.DataAccess = DataAccessLevel.DataRead;       // adds tools/resources that read document data
        // o.DataAccess = DataAccessLevel.DataReadWrite; // adds tools that modify data (e.g. mongodb.clean)
    });
});
```

Each tool/resource is tagged below with its required level. Anything above the configured level is filtered out of `tools/list` / `resources/list` and rejected at `tools/call` / `resources/read` with an `IsError` response.

> **Upgrading from `Tharga.MongoDB.Mcp` 2.10.x:** the default level is `Metadata`, which means `mongodb://monitoring` no longer surfaces unless you opt in to `DataAccessLevel.DataRead`. Calls also embed query filter values, hence the gating.

### Resources (System scope)
| URI | Level | Description |
|---|---|---|
| `mongodb://collections` | Metadata | List of collections with stats, index info, and clean status |
| `mongodb://clients` | Metadata | Connected remote monitoring agents |
| `mongodb://monitoring` | DataRead | Recent and slow calls, summaries, error summary, connection pool state, and `inFlightCalls` (queued vs executing, grouped by collection/function/filter) — calls embed filter values |

### Tools (System scope)
| Tool | Level | Args |
|---|---|---|
| `mongodb.touch` | Metadata | `databaseName`, `collectionName`, optional `configurationName` |
| `mongodb.rebuild_index` | Metadata | `databaseName`, `collectionName`, optional `configurationName`, `force` |
| `mongodb.restore_all_indexes` | Metadata | optional `configurationName` / `databaseName` filters; returns total/succeeded/failed/skipped counts |
| `mongodb.drop_index` | Metadata | `databaseName`, `collectionName`, optional `configurationName`; drops indexes not declared in code |
| `mongodb.reset_cache` | Metadata | (no args) resets the in-memory monitor cache |
| `mongodb.clear_call_history` | Metadata | (no args) clears recent + slow call history |
| `mongodb.find_duplicates` | DataRead | `databaseName`, `collectionName`, `indexName`, optional `configurationName`; returns duplicate-key tuples |
| `mongodb.explain` | DataRead | `callKey` (Guid string); returns explain plan including the original query filter |
| `mongodb.clean` | DataReadWrite | `databaseName`, `collectionName`, optional `configurationName`, `cleanGuids`; deletes orphaned/invalid documents |
| `mongodb.get_document` | DataRead | `databaseName`, `collectionName`, `id`, optional `configurationName`; returns the raw document as MongoDB Extended JSON. `id` is auto-detected as Guid → ObjectId → string |
| `mongodb.list_documents` | DataRead | `databaseName`, `collectionName`, optional `configurationName`, `limit` (default 20, max 200), `skip`, `filter` (JSON string), `sort` (JSON string `{"field":1}`); returns up to N raw documents |
| `mongodb.compare_schema` | DataRead | `databaseName`, `collectionName`, optional `configurationName`, `sampleSize` (default 50, max 500); three-way diff between the C# entity properties, registered entity-type names, and the field set observed in sampled documents |
| `mongodb.get_monitor_clients` | Metadata | (no args) connected monitoring agents with source, machine, version, connection state, and forwarding config |
| `mongodb.get_per_pool_queue_state` | Metadata | (no args) live per-pool queue/executing state across this server and every reporting agent (keyed `{source}::{serverKey}`), plus active subscriptions |
| `mongodb.get_client_communication` | Metadata | `sourceName`; recent inbound/outbound message log for one agent |
| `mongodb.hold_live_subscription` | Metadata | `seconds` (default 5, max 60); opens a live-monitoring subscription for N seconds, then returns the per-pool queue state observed — drives queue-metric forwarding headlessly. Requires the monitor server |

Providers are registered with `McpScope.System`, so they are only exposed on the system-level MCP endpoint.

### Live-monitoring diagnostics

Agents forward queue metrics only while a live subscriber is present (normally the **Calls → Queue** view). The monitoring tools above let an agent reproduce and observe that flow without a browser: `mongodb.hold_live_subscription` opens a subscription for N seconds (connected agents begin forwarding), then `mongodb.get_per_pool_queue_state` confirms the metrics arriving, while `mongodb.get_monitor_clients` / `mongodb.get_client_communication` show which agents are connected and the messages crossing the wire.

### Document inspection

`mongodb.get_document`, `mongodb.list_documents`, and `mongodb.compare_schema` let an authorized agent inspect raw documents and detect schema drift via MCP — the same diagnostic loop typically done via `mongosh` on a production shell, available through the existing MCP plumbing instead.

- All three are `DataRead` — they're hidden by default. Set `o.DataAccess = DataAccessLevel.DataRead` to opt in.
- Documents are returned as MongoDB Extended JSON: the exact shape stored in MongoDB, never round-tripped through the C# serializer.
- `compare_schema` reflects on the entity type's public properties (resolved from the registered collection class) and compares against the field set in the sample. Top-level fields only — nested document drift is a known limitation and may be addressed in a follow-up.
- Per-tenant databases (`DatabasePart` / per-team DBs) work directly: pass the resolved `databaseName` from `mongodb://collections`. No special "part" parameter needed.
- Remote-only collections (`Registration.NotInCode`) are not yet supported — these tools throw a clear error. Adding remote routing requires extending `IRemoteActionDispatcher` and the Monitor.Server pipeline; planned as a follow-up.

### Atlas Administration tools

When `MongoDbMcpOptions.Atlas` is set, the package also exposes a curated read-only slice of the MongoDB Atlas Administration API as MCP tools — diagnose Atlas Performance Advisor suggestions or open alerts from any MCP client without re-implementing the Atlas integration per consumer.

```csharp
services.AddThargaMcp(mcp => mcp.AddMongoDB(o =>
{
    o.Atlas = new MongoDbApiAccess
    {
        PublicKey  = "<atlas-public-key>",
        PrivateKey = "<atlas-private-key>",
        GroupId    = "<atlas-project-id>",
    };
}));
```

| Tool | Level | Args |
|---|---|---|
| `atlas.list_clusters` | Metadata | (no args) — returns clusters in the configured Atlas project |
| `atlas.get_performance_advisor_suggestions` | Metadata | `clusterName` (from `atlas.list_clusters`) — returns the suggested-index list per the Atlas UI |
| `atlas.get_open_alerts` | Metadata | (no args) — returns currently-firing alerts in the project |

All three target Atlas Administration API v2. Auth is HTTP Digest via the public/private API key pair, the same pattern used by the firewall integration in `Tharga.MongoDB`. Leaving `Atlas` unset keeps the surface entirely opt-in — no Atlas tools are advertised unless the option is configured.

---

## Aggregation Queries

Server-side aggregation methods let you compute values without loading documents into memory.

### Estimated Count
```csharp
// Fast metadata-based count (no collection scan)
var count = await collection.EstimatedCountAsync();
```

### Sum, Avg, Min, Max
```csharp
// Sum a numeric field
var total = await collection.SumAsync(x => x.Amount);

// Average with filter
var avg = await collection.AvgAsync(x => x.Amount, x => x.Category == "A");

// Min / Max
var min = await collection.MinAsync<decimal>(x => x.Amount);
var max = await collection.MaxAsync<decimal>(x => x.Amount);
```

All methods accept an optional `predicate` to filter documents before aggregation, and a `CancellationToken`.

For arbitrary aggregation pipelines, use `ExecuteAsync` (materialising) or `ExecuteManyAsync` (streaming) which both give direct access to `IMongoCollection<T>`.

---

## Custom queries

Two methods hand you the underlying `IMongoCollection<T>` so you can write queries the repository doesn't expose directly — projections, aggregation pipelines, etc. Both run through the library's index management, concurrency limiter, and admin-UI call tracking.

### `ExecuteAsync` — materialised result
Use when the result fits comfortably in memory.
```csharp
var names = await collection.ExecuteAsync(
    c => c.Find(Builders<MyEntity>.Filter.Empty)
          .Project(x => x.Name)
          .ToListAsync(),
    Operation.Read);
```

### `ExecuteManyAsync` — streaming cursor
Use when the result may be large. Returns `IAsyncEnumerable<T>` so the caller iterates without materialising the whole set. The factory returns an `IAsyncCursor<T>`; the library takes a limiter slot around the initial open and around each `MoveNextAsync` (batch fetch) so the driver connection pool isn't oversubscribed by long-running streams. Batch size is controlled by the caller on the query itself (`BatchSize` in `FindOptions`/`AggregateOptions`). Always a read — no `Operation` parameter.

Find with projection:
```csharp
await foreach (var name in collection.ExecuteManyAsync(
    (c, ct) => c.FindAsync(
        Builders<MyEntity>.Filter.Empty,
        new FindOptions<MyEntity, string>
        {
            Projection = Builders<MyEntity>.Projection.Expression(x => x.Name),
            BatchSize = 500
        },
        ct),
    cancellationToken))
{
    Process(name);
}
```

Aggregation pipeline:
```csharp
var pipeline = PipelineDefinition<MyEntity, BsonDocument>.Create(
    "{ $match: { Active: true } }",
    "{ $group: { _id: '$Category', count: { $sum: 1 } } }");

await foreach (var doc in collection.ExecuteManyAsync(
    (c, ct) => c.AggregateAsync(pipeline, new AggregateOptions { BatchSize = 500 }, ct),
    cancellationToken))
{
    Process(doc);
}
```

---

## Keyset pagination

`GetPageAsync` and `GetPageProjectionAsync` page through a collection by *cursor*, not by `skip`/`limit`. Cost is O(log N) per page regardless of how deep the page sits — there is no skip penalty when paging into the millions of documents and no spike when a user clicks "jump to last." Single-column sort with a `_id` tiebreaker; total count is intentionally not part of the result and should be fetched separately via `CountAsync(predicate)` (cache it client-side; it changes far less often than the page).

### Basic usage

```csharp
var first = await collection.GetPageAsync(
    pageSize: 25,
    position: PagePosition.First,
    sortBy: e => e.CreatedAt,
    ascending: false);

// "Next page" — feed the previous page's LastCursor back in
var next = await collection.GetPageAsync(
    pageSize: 25,
    position: PagePosition.After(first.LastCursor),
    sortBy: e => e.CreatedAt,
    ascending: false);

// "Previous page" — feed the current page's FirstCursor in via Before
var prev = await collection.GetPageAsync(
    pageSize: 25,
    position: PagePosition.Before(next.FirstCursor),
    sortBy: e => e.CreatedAt,
    ascending: false);
```

`PagePosition` has four factories:

| Position | Use |
|---|---|
| `PagePosition.First` | First page in sort order |
| `PagePosition.Last` | Final `pageSize` items in sort order (note: not the partial last page — the trailing `pageSize` items, slid to align with the page boundary) |
| `PagePosition.After(cursor, pageStep = 0)` | Page after the cursor; `pageStep` skips that many extra pages forward |
| `PagePosition.Before(cursor, pageStep = 0)` | Page before the cursor; `pageStep` skips that many extra pages backward |

`CursorPage<T>` exposes `Items`, `FirstCursor`, `LastCursor`, `HasNext`, `HasPrevious`. The cursors are opaque, URL-safe strings (Base64URL of a small BSON doc) — store them in query strings, hidden form fields, or component state.

### Sort + filter

```csharp
var page = await collection.GetPageAsync(
    pageSize: 50,
    position: PagePosition.After(cursor),
    predicate: e => e.Status == "active",
    sortBy: e => e.Name,
    ascending: true);
```

Predicates compose with the keyset filter — the page is the items matching `predicate` strictly past `cursor` in the sort order.

### Index guidance

For each `(sortBy, ascending)` you page on, create the compound index `{ sortField: ±1, _id: ±1 }`:

```csharp
public override IEnumerable<CreateIndexModel<MyEntity>> Indices =>
[
    new(Builders<MyEntity>.IndexKeys.Ascending(e => e.CreatedAt).Ascending(e => e.Id),
        new CreateIndexOptions { Name = "createdAt_id" }),
];
```

Without the compound index the query still works but degrades to a sort + scan. With it, the planner uses an `IXSCAN` that walks straight to the cursor boundary and reads only the page-size's worth of documents.

### Total count

```csharp
var total = await collection.CountAsync(e => e.Status == "active");
```

`CountAsync` is a separate query because counts are typically far cheaper to cache than pages — once per filter-change, not once per page-flip. Don't bake it into the paging hot path.

### `CursorPager<TEntity, TKey>` — easy path for grids

Most grid components (Radzen `RadzenDataGrid`, MudBlazor `MudDataGrid`, etc.) emit a `(skip, pageSize)` request on user navigation. `CursorPager` adapts that shape to the keyset API: it tracks the previous page's cursors, decodes the skip-delta into the appropriate `PagePosition`, falls back to skip-based `GetManyAsync` when the user does an arbitrary jump (e.g. clicking "page 17 of 200"), and re-issues cursors from the fallback's boundary documents so the next prev/next call resumes the keyset path.

```csharp
private CursorPager<Order, ObjectId> _pager;

protected override void OnInitialized()
{
    _pager = new CursorPager<Order, ObjectId>(_orders);
}

private async Task LoadDataAsync(LoadDataArgs args)
{
    var (items, total) = await _pager.LoadAsync(
        skip: args.Skip ?? 0,
        pageSize: args.Top ?? 25,
        predicate: BuildPredicate(args.Filter),
        sortBy: o => o.CreatedAt,
        ascending: false);

    _orders = items;
    _totalCount = (int)total;
}

private void OnFilterChanged() => _pager.Reset();
```

`CursorPager` caches the total count per `(predicate, sortBy, ascending)` cache key — when any of those change it re-runs `CountAsync` and clears the cursors. `Reset()` clears all state (use it when the underlying data is known to have changed underfoot).

### Manual path

When you need full control — e.g. cursor links shared between users, or persistence across sessions — work with `GetPageAsync` directly and stash `FirstCursor`/`LastCursor` wherever fits your application. `CursorToken` round-trips through `ToString()` and `CursorToken.Parse(string)` so it's safe to put in URLs, cookies, or hidden form fields. `CursorToken.From(entity, sortBy, ascending)` lets you mint a cursor pointing at any specific document — useful when restoring grid state from a deep link.

```csharp
var anchorToken = CursorToken.From<Order, ObjectId>(anchor, o => o.CreatedAt, ascending: false);
var page = await collection.GetPageAsync(25, PagePosition.After(anchorToken),
    sortBy: o => o.CreatedAt, ascending: false);
```

Cursors are sort-bound: passing one issued for `sortBy: x => x.Name` to a call sorting by `x => x.CreatedAt` throws `InvalidOperationException`. This is intentional — silently re-sorting would return wrong results.

---

## Execute Limiter
The built-in execute limiter queues database operations to prevent exhausting the MongoDB connection pool.
It is always sized to the connection's `MaxConnectionPoolSize` (the driver default is 100) — no configuration needed.
Set `MaxPoolSize` in the connection string, or use [`MaxPoolSizeOverride`](#per-configuration-pool-size) to control it per configuration.

Operations sharing the same connection pool — the same set of servers **and** the same `MaxConnectionPoolSize` — share a single queue, regardless of how many configuration names point to that cluster. Configurations on the same cluster with **different** pool sizes get their own `MongoClient` and their own queue.

### Configuration by `appsettings.json`
```json
"MongoDB": {
  "Limiter": {
    "Enabled": true
  }
}
```

### Configuration by code
```csharp
builder.AddMongoDB(o =>
{
    o.Limiter = new ExecuteLimiterOptions
    {
        Enabled = true
    };
});
```

| Setting | Default | Description |
|---|---|---|
| `Enabled` | `true` | Enable or disable the limiter. When disabled, operations run without any concurrency restriction. The concurrency limit is always derived from `MaxConnectionPoolSize`. |

The monitor surfaces the limiter **per pool** (one per cluster): queue/exec and depth/wait graphs per pool, what is queued vs executing right now (`GetInFlightCalls()` / the MCP `inFlightCalls` resource), and actual open connections per cluster vs a configured limit (`GetClusterConnectionSummary()` + `Monitor.ClusterConnectionLimit`). See the [monitoring docs](https://github.com/Tharga/MongoDB/blob/master/docs/articles/monitoring.md).

### Per-configuration pool size
`MaxConnectionPoolSize` is part of the `MongoClient` cache key, so two configurations pointing at the same cluster with different `MaxPoolSize` values each get their own client with the correct pool size — they no longer silently share whichever client was created first.

To set the pool size per configuration name without separate connection strings, provide `MaxPoolSizeOverride`. It is applied to the connection URL **before** the `MongoClient` is created, so both the driver and the execute limiter see the same value:

```csharp
builder.AddMongoDB(o =>
{
    o.DefaultConfigurationName = "Aggregator";
    o.MaxPoolSizeOverride = (serviceProvider, configName, connectionStringPoolSize) => configName switch
    {
        "Aggregator"  => Task.FromResult(500),
        "Integration" => Task.FromResult(50),
        _             => Task.FromResult(connectionStringPoolSize) // pass through if unknown
    };
});
```

The delegate receives the service provider, the configuration name, and the `MaxPoolSize` already present in the connection string (or the driver default of 100 if absent). It is `async` and invoked once per configuration when the URL is built — never on the execution hot path.

---

## MongoDB Result Limit
It is possible to se t a hard limit for the number of documents returned. If the limit is reached `ResultLimitException` is thrown.
For large result-sets, use `GetManyAsync` with an explicit `Limit` to fetch a bounded page, or stream through `GetAsync` / `GetProjectionAsync` / `ExecuteManyAsync` — all three use a driver cursor under the hood and stream batches without paying a skip penalty.

```
{
  "MongoDB": {
    "ResultLimit": 500
  }
}
```
