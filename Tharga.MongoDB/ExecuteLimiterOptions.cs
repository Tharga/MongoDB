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
    /// Maximum number of concurrent database operations allowed per connection pool.
    /// Excess operations are queued until a slot becomes available.
    /// When null (default), the limit is auto-detected from <c>MaxConnectionPoolSize</c>.
    /// </summary>
    public int? MaxConcurrent { get; set; }
}
