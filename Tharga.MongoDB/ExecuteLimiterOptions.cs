namespace Tharga.MongoDB;

public record ExecuteLimiterOptions
{
    /// <summary>
    /// Enable or disable the execute limiter.
    /// When disabled, all database operations execute without any concurrency restriction.
    /// By default, the limiter is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;
}
