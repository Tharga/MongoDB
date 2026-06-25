using System;
using System.Threading;
using System.Threading.Tasks;
using Tharga.MongoDB.Disk;

namespace Tharga.MongoDB;

internal interface IExecuteLimiter
{
    Task<(T Result, ExecuteInfo Info)> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, string serverKey, int maxConnectionPoolSize, ExecuteCallContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// Lightweight metadata about a call entering the limiter, so the limiter can report what is actually
/// queued vs executing (per-pool) for diagnostics. Carries a <see cref="FilterProvider"/> that renders the
/// query filter lazily — it is only invoked when someone inspects the in-flight calls (e.g. via MCP), never
/// on the execution hot path.
/// </summary>
internal sealed class ExecuteCallContext
{
    public Guid CallKey { get; init; }
    public string ConfigurationName { get; init; }
    public string DatabaseName { get; init; }
    public string CollectionName { get; init; }
    public string FunctionName { get; init; }
    public Operation Operation { get; init; }
    public Func<string> FilterProvider { get; init; }
}