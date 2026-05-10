namespace Tharga.MongoDB.Lockable;

/// <summary>
/// Discriminator passed to the unified error handler on the <c>EntityScopeExtensions.ExecuteAsync</c> overload
/// that takes <c>Action&lt;LockableErrorKind, Exception&gt;</c>, so callers can route every error type from a
/// single callback without try/catching individual exception classes.
/// </summary>
public enum LockableErrorKind
{
    /// <summary>Exception thrown by the user-provided <c>func</c> before commit.</summary>
    HandlerError,

    /// <summary>The lock timed out before <c>CommitAsync</c> completed (<see cref="LockExpiredException"/>).</summary>
    LockExpired,

    /// <summary>The lock was already released before commit was attempted (<see cref="LockAlreadyReleasedException"/>).</summary>
    LockAlreadyReleased,

    /// <summary>Other commit failure — typically <see cref="CommitException"/> or a transient I/O error.</summary>
    CommitError,
}
