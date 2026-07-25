using System.Threading;
using System.Threading.Tasks;

namespace Tharga.MongoDB.Interception;

/// <summary>
/// A pre-call hook that runs before a repository operation reaches the MongoDB driver, and can
/// reject it.
/// <para>
/// Register with <c>DatabaseOptions.AddCollectionInterceptor&lt;T&gt;()</c>. Interceptors run in
/// registration order and the first rejection short-circuits the rest.
/// </para>
/// <para>
/// This is the veto-capable counterpart to the static observational
/// <see cref="RepositoryCollectionBase.ActionEvent"/>. Prefer this for policy — it resolves from DI,
/// so it is configured per container and does not leak between tests in the same process. Prefer
/// <c>ActionEvent</c> for telemetry, which cannot and should not change what the caller gets.
/// </para>
/// <para>
/// The package is deliberately ignorant of what an interceptor decides. It knows nothing of
/// authorization, tenancy or latency; it resolves whatever is registered and runs it.
/// </para>
/// </summary>
public interface ICollectionInterceptor
{
    /// <summary>
    /// Which point(s) in an operation's lifetime this interceptor wants to run at. Defaults to
    /// <see cref="InterceptionPoint.Invocation"/>, which is what a policy gate wants; override only
    /// to opt into <see cref="InterceptionPoint.Enumeration"/> as well.
    /// </summary>
    InterceptionPoint Points => InterceptionPoint.Invocation;

    /// <summary>
    /// Called before the operation runs. Return <see cref="InterceptDecision.Proceed"/> to allow it,
    /// or <see cref="InterceptDecision.Reject"/> to block it.
    /// <para>
    /// Throwing also blocks the operation, and the exception propagates to the caller unchanged.
    /// Prefer <see cref="InterceptDecision.Reject"/> — it gives callers a single documented
    /// exception type to catch — and throw only when a meaningful domain exception already exists
    /// and laundering it through a string reason would lose information.
    /// </para>
    /// <para>
    /// This runs on the operation's hot path. Keep it cheap, and do not call back into a repository
    /// collection from here.
    /// </para>
    /// </summary>
    /// <param name="call">What is about to run.</param>
    /// <param name="cancellationToken">The cancellation token of the intercepted operation.</param>
    ValueTask<InterceptDecision> BeforeCallAsync(CollectionCallInfo call, CancellationToken cancellationToken = default);
}
