# Collection interceptors

A pre-call hook that runs before a repository operation reaches the MongoDB driver, and can reject it. Register one and you have a guarantee that no database call happens without your own policy layer having run first — protection against *forgetting* to authorize, rather than against a determined bypass.

The package stays mechanism-only. It knows nothing about teams, tenancy, authorization or latency; it resolves whatever interceptors are registered and runs them in order.

## Basic usage

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

The type is resolved from DI, so an interceptor with its own dependencies works normally — register it yourself first and the package will use your registration rather than replacing it. There is also an instance overload for interceptors that need no dependencies:

```csharp
o.AddCollectionInterceptor(new DelayInterceptor(TimeSpan.FromMilliseconds(250)));
```

Interceptors run in registration order, and the first rejection short-circuits the rest.

## Rejecting a call

`InterceptDecision.Reject(reason)` blocks the operation and throws `CollectionAccessDeniedException`, which carries both the reason and the `CollectionCallInfo` that was rejected:

```csharp
catch (CollectionAccessDeniedException ex)
{
    logger.LogWarning("Blocked {Operation} on {Collection}: {Reason}",
        ex.Call.Operation, ex.Call.CollectionName, ex.Reason);
}
```

Throwing from an interceptor also blocks the operation, and your exception propagates to the caller unchanged. Prefer `Reject` — it gives callers a single documented exception type to catch — and throw only when a meaningful domain exception already exists and laundering it through a string reason would lose information.

### When the caller sees the rejection

A **synchronous** interceptor — which is what an ambient-context check is — rejects at the *call site*, even for operations returning `IAsyncEnumerable`:

```csharp
var stream = collection.GetAsync(x => x.Active);  // throws here
```

An interceptor that genuinely yields (an `await` that does not complete synchronously) cannot be resolved before the method returns, so its rejection surfaces on first enumeration instead. Either way the operation never reaches the driver.

## What an interceptor sees

`CollectionCallInfo` describes the operation about to run:

| Member | Notes |
|---|---|
| `CollectionName` | Resolved name, after any `DatabaseContext` override |
| `Operation` | The repository method name, e.g. `GetOneAsync` |
| `OperationType` | `Create` / `Read` / `Update` / `Delete` |
| `EntityType` | The entity type the collection stores |
| `Point` | Which timing point this invocation represents |
| `ConfigurationName`, `DatabaseName`, `DatabaseContext` | Where the call resolves to |

**Key on `OperationType`, not on the `Operation` string,** when you need to distinguish reads from writes. `Operation` is a diagnostic label: operations on a lockable collection report the underlying disk operation rather than the semantic wrapper, so a `PickForUpdateAsync` appears as `UpdateOneAsync` — the write that actually runs.

## Timing points

An interceptor declares which point(s) it wants via `Points`, defaulting to `Invocation`:

```csharp
public InterceptionPoint Points => InterceptionPoint.Invocation | InterceptionPoint.Enumeration;
```

**`Invocation`** (default) fires when the calling code invokes the operation, before any database work is scheduled. This is the point a policy gate wants: it runs while the caller's ambient context is still in scope. It is also the only meaningful point for operations returning `IAsyncEnumerable`, whose database work would otherwise happen at enumeration time — potentially much later, on a different logical call stack.

**`Enumeration`** fires inside the iterator, once per stream at cursor open, at the point the driver work actually happens. Use it only for concerns that must affect the observed timing or ordering of a deferred result, such as a development latency simulator. It is not a substitute for `Invocation` in a policy gate, and for non-deferred operations it never fires.

An interceptor declaring both is called twice for a deferred read and can tell the calls apart by `CollectionCallInfo.Point`.

## Coverage

Every **public data operation** is intercepted, on both `DiskRepositoryCollectionBase` and `LockableRepositoryCollectionBase`, however the collection was obtained — through `ICollectionProvider`, through DI, or constructed directly with an `IMongoDbServiceFactory`. That last route matters: a collection that takes the factory in its constructor and never touches `ICollectionProvider` is still covered, which is why decorating the provider is not an equivalent approach.

For lockable collections this includes the lock lifecycle — acquire, extend, commit, release — because each of those is a database write routed through the same disk operations. The escape hatch `ExecuteAsync(Func<IMongoCollection<TEntity>, …>)` is covered too, so even raw driver access from consumer code passes through the chain.

**Not intercepted:** the internal index and maintenance plumbing — index assurance, index drop, collection clean, and the monitor's own metadata reads. These do reach the driver, but they are internal to the package (a consumer cannot call them) and are driven by the monitor's admin surface, which has its own authorization.

## Cost

When nothing is registered the path is a field read and a branch. It allocates nothing, and `CollectionCallInfo` is never constructed. Registering an interceptor for one timing point leaves the other point free, and within a single call the `CollectionCallInfo` is built once and shared across the chain. This is asserted by tests measuring allocated bytes, not merely intended.

Interceptors run on the operation's hot path — keep them cheap, and **do not call back into a repository collection from one**.

## Relationship to `ActionEvent`

`RepositoryCollectionBase.ActionEvent` is the observational channel: a static event, raised for telemetry, that cannot change what the caller gets. It stays exactly as it was.

`ICollectionInterceptor` is the policy channel: resolved from DI, so it is configured per container and does not leak between hosts or between tests in the same process — and it can veto.

Use `ActionEvent` to watch. Use an interceptor to decide.

## Caveats

- **Interceptors are effectively singletons.** They are resolved once when the service factory is built. Keep per-operation state in an `AsyncLocal`, not in the interceptor — in Blazor Server a DI scope is the circuit lifetime, not the operation, so a scoped holder would go stale on a context switch.
- **`CreateStrategy.DropEmpty` nests a call.** When a delete empties a collection under that strategy, the package drops the collection, which raises its own `DropCollectionAsync` interception inside the delete. An interceptor that permits the delete but rejects the drop will throw *after* the delete has already been applied.
- **Rejected calls are invisible to the monitor.** The chain runs before the call is registered, so a rejected operation does not appear in the monitor, call history or `/developer/database`. It never touched the database. Audit rejections in your interceptor, which is where the reason lives.
