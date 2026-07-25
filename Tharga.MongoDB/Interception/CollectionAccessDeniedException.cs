using System;

namespace Tharga.MongoDB.Interception;

/// <summary>
/// Thrown when a registered <see cref="ICollectionInterceptor"/> returned
/// <see cref="InterceptDecision.Reject"/> for an operation. The operation did not run and the
/// database was never touched.
/// <para>
/// An interceptor that throws its own exception instead of rejecting propagates that exception
/// unchanged — this type is only produced by the <see cref="InterceptDecision.Reject"/> path.
/// </para>
/// </summary>
public sealed class CollectionAccessDeniedException : Exception
{
    internal CollectionAccessDeniedException(string reason, CollectionCallInfo call)
        : base($"Access to '{call.CollectionName}.{call.Operation}' was denied by an interceptor: {reason}")
    {
        Reason = reason;
        Call = call;
    }

    /// <summary>The reason supplied to <see cref="InterceptDecision.Reject"/>.</summary>
    public string Reason { get; }

    /// <summary>The operation that was rejected.</summary>
    public CollectionCallInfo Call { get; }
}
