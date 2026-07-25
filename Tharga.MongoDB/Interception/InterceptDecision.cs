namespace Tharga.MongoDB.Interception;

/// <summary>
/// The verdict an <see cref="ICollectionInterceptor"/> returns for a single operation.
/// <para>
/// Declared as a readonly struct so the common <see cref="Proceed"/> path allocates nothing on the
/// call hot path. The default value is <see cref="Proceed"/>.
/// </para>
/// </summary>
public readonly record struct InterceptDecision
{
    private InterceptDecision(bool isRejected, string reason)
    {
        IsRejected = isRejected;
        Reason = reason;
    }

    /// <summary>
    /// Allow the operation to continue. Remaining interceptors still run.
    /// </summary>
    public static InterceptDecision Proceed => default;

    /// <summary>
    /// Reject the operation. The pipeline short-circuits — no further interceptor runs, the database
    /// is never touched, and a <see cref="CollectionAccessDeniedException"/> carrying
    /// <paramref name="reason"/> is thrown to the caller.
    /// </summary>
    /// <param name="reason">
    /// Why the call was rejected. Surfaced on the exception, so write it for whoever reads the
    /// resulting stack trace.
    /// </param>
    public static InterceptDecision Reject(string reason) => new(true, reason);

    /// <summary>True when this decision rejects the operation.</summary>
    public bool IsRejected { get; }

    /// <summary>The rejection reason, or null when the decision is <see cref="Proceed"/>.</summary>
    public string Reason { get; }
}
