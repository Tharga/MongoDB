using System;

namespace Tharga.MongoDB.Lockable.Renewable;

/// <summary>
/// Tuning for <c>StartKeepAlive</c>. A background loop periodically extends the lease so a short
/// lease survives long-running work without pinning the document forever if the owner dies.
/// </summary>
public record LockKeepAliveOptions
{
    /// <summary>
    /// How often the keep-alive loop attempts an extension. When <c>null</c>, defaults to one third
    /// of the lease's original timeout, so each lease has three chances to renew before it expires.
    /// </summary>
    public TimeSpan? Interval { get; init; }

    /// <summary>
    /// How far into the future each renewal pushes <c>ExpireTime</c>. When <c>null</c>, defaults to
    /// the lease's original timeout.
    /// </summary>
    public TimeSpan? Extension { get; init; }

    /// <summary>
    /// Anti-zombie cap. Once the keep-alive loop has been running this long, it stops renewing and
    /// lets the lease expire — protects against a stuck owner that never releases. Defaults to 4 hours.
    /// </summary>
    public TimeSpan MaxTotalDuration { get; init; } = TimeSpan.FromHours(4);

    /// <summary>
    /// Invoked when a renewal attempt fails. For transient failures the loop continues; for terminal
    /// failures (lock lost, strict-TTL expiry) the loop stops after invoking this callback.
    /// </summary>
    public Action<Exception> OnRenewalFailure { get; init; }
}
