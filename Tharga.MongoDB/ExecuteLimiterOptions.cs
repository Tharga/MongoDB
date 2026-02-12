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
    /// Maximum number of concurrent database operations allowed per key.
    /// Excess operations are queued until a slot becomes available.
    /// Default is 20.
    /// </summary>
    public int MaxConcurrent { get; set; } = 20;
}
