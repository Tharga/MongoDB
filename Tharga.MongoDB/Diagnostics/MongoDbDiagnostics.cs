namespace Tharga.MongoDB.Diagnostics;

/// <summary>
/// Names of the diagnostic sources this library publishes.
/// </summary>
public static class MongoDbDiagnostics
{
    /// <summary>
    /// The <see cref="System.Diagnostics.ActivitySource"/> name carrying MongoDB dependency spans. Register it
    /// with an OpenTelemetry tracer provider to receive them.
    /// <code>
    /// builder.Services.AddOpenTelemetry()
    ///     .WithTracing(t =&gt; t.AddSource(MongoDbDiagnostics.ActivitySourceName));
    /// </code>
    /// </summary>
    public const string ActivitySourceName = "Tharga.MongoDB";
}
