using System;

namespace Tharga.MongoDB;

public record ExecuteLimiterOptions
{
    /// <summary>
    /// Enable or disable the execute limiter.
    /// When disabled, all database operations execute without any concurrency restriction.
    /// By default, the limiter is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The greatest number of operations that may be <b>waiting</b> for a slot on one connection pool.
    /// A further operation is rejected with <see cref="ExecuteLimiterQueueFullException"/> rather than queued,
    /// so a burst becomes backpressure the caller can retry or shed instead of unbounded memory growth.
    /// Default is null — unbounded, which is the historical behaviour.
    /// <para>
    /// Only operations that actually have to wait count against this. One that finds a free slot executes
    /// immediately whatever the limit is, so <c>0</c> means "never queue" rather than "never run".
    /// </para>
    /// </summary>
    public int? MaxQueueLength { get; set; }

    /// <summary>
    /// The longest a single operation may wait for a slot before it is rejected with
    /// <see cref="ExecuteLimiterQueueTimeoutException"/>. Default is null — wait indefinitely.
    /// <para>
    /// Complementary to <see cref="MaxQueueLength"/> rather than a substitute: a timeout alone still lets
    /// <c>rate × timeout</c> operations accumulate under a sustained flood.
    /// </para>
    /// </summary>
    public TimeSpan? QueueTimeout { get; set; }
}
