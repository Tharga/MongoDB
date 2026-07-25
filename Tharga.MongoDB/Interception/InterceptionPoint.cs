using System;

namespace Tharga.MongoDB.Interception;

/// <summary>
/// The point in an operation's lifetime at which an <see cref="ICollectionInterceptor"/> runs.
/// An interceptor declares the points it wants via <see cref="ICollectionInterceptor.Points"/>
/// and may declare both.
/// </summary>
[Flags]
public enum InterceptionPoint
{
    /// <summary>
    /// Fires when the calling code invokes the operation, before any database work is scheduled.
    /// <para>
    /// This is the default and the correct point for a policy or authorization gate: it runs while
    /// the caller's ambient context is still in scope. It is also the only meaningful point for
    /// operations returning <c>IAsyncEnumerable</c>, whose database work would otherwise happen at
    /// enumeration time — potentially long after, and on a different logical call stack.
    /// </para>
    /// </summary>
    Invocation = 1,

    /// <summary>
    /// Fires inside the iterator of a deferred (<c>IAsyncEnumerable</c>) operation, at the point the
    /// cursor is opened and the driver work actually happens.
    /// <para>
    /// Use this only for concerns that must affect the observed timing or ordering of a deferred
    /// result, such as a development latency simulator. It is not a substitute for
    /// <see cref="Invocation"/> in a policy gate — by the time it fires, the caller's ambient
    /// context may be gone. For non-deferred operations this point never fires.
    /// </para>
    /// </summary>
    Enumeration = 2
}
