namespace Tharga.MongoDB.Configuration;

/// <summary>
/// Controls how <see cref="MongoDbRegistrationExtensions.UseMongoDB"/> reacts when a configured
/// connection is unreachable during the startup connectivity pre-check.
/// </summary>
public enum StartupConnectivityMode
{
    /// <summary>
    /// Default. If any configured connection is unreachable at startup, the failure is logged
    /// (<c>LogCritical</c>), the <see cref="UseMongoOptions.StartupFailureCallback"/> is awaited
    /// (so telemetry can be flushed), and a <see cref="MongoStartupConnectivityException"/> is
    /// thrown — the process still exits, but the failure is observable. Back-compatible with the
    /// previous fail-fast behaviour, minus the unhandled, untelemetered abort.
    /// </summary>
    FailFast,

    /// <summary>
    /// Start the host even when a configured connection is unreachable. The failure is logged and
    /// the <see cref="UseMongoOptions.StartupFailureCallback"/> is awaited, but startup continues.
    /// <see cref="IMongoDbConnectivityState"/> reports the connection as unhealthy until
    /// connectivity is restored, so a readiness/health endpoint can surface the degradation while
    /// the rest of the app keeps running and telemetry keeps flowing.
    /// </summary>
    Degrade
}
