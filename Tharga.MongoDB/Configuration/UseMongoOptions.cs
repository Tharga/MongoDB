using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace Tharga.MongoDB.Configuration;

public record UseMongoOptions
{
    /// <summary>
    /// Controls behaviour when a configured connection is unreachable during the startup
    /// connectivity pre-check. Default <see cref="StartupConnectivityMode.FailFast"/> — the
    /// process still exits on an unreachable database (back-compatible), but the failure is now
    /// logged (<c>LogCritical</c>), flushed via <see cref="StartupFailureCallback"/>, and thrown
    /// as a <see cref="MongoStartupConnectivityException"/> rather than an unhandled, untelemetered
    /// abort. Set to <see cref="StartupConnectivityMode.Degrade"/> to start degraded instead and
    /// surface the failure through <see cref="IMongoDbConnectivityState"/> / a health endpoint.
    /// <para>The pre-check is skipped when <c>DatabaseOptions.ReadyCallback</c> is configured
    /// (connection strings arrive later), mirroring the firewall-open skip.</para>
    /// </summary>
    public StartupConnectivityMode StartupConnectivity { get; set; } = StartupConnectivityMode.FailFast;

    /// <summary>
    /// Total number of attempts per connection during the startup connectivity pre-check.
    /// Unreachable connections are retried with exponential backoff (see
    /// <see cref="StartupConnectivityRetryDelay"/>); reachable ones are not re-probed. Default 3.
    /// Set to 1 to disable retrying.
    /// </summary>
    public int StartupConnectivityRetryCount { get; set; } = 3;

    /// <summary>
    /// Initial delay before the first retry of the startup connectivity pre-check, doubled on each
    /// subsequent retry (capped at one minute). Default 2 seconds.
    /// </summary>
    public TimeSpan StartupConnectivityRetryDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Invoked (and awaited) when the startup connectivity pre-check finds one or more unreachable
    /// connections — before a <see cref="StartupConnectivityMode.FailFast"/> rethrow or a
    /// <see cref="StartupConnectivityMode.Degrade"/> continuation. The library logs the failure via
    /// <c>ILogger</c> itself; use this hook to flush your telemetry pipeline so the failure is not
    /// lost on exit, e.g. <c>telemetryClient.Flush(); await Task.Delay(TimeSpan.FromSeconds(5));</c>
    /// for Application Insights, or <c>tracerProvider.ForceFlush()</c> for OpenTelemetry. Optional.
    /// </summary>
    public Func<MongoStartupFailure, IServiceProvider, Task> StartupFailureCallback { get; set; }

    /// <summary>
    /// Wait for the UseMongoDB-method to complete before continuing.
    /// By default, this is false.
    /// </summary>
    public bool WaitToComplete { get; set; }

    /// <summary>
    /// Open atlas firewall for current IP if needed.
    /// If this is false, the firewall will be opened the first time they are used.
    /// By default, this is true.
    /// This is only valid if there is configuration for the firewall.
    /// </summary>
    public bool OpenFirewall { get; set; }

    /// <summary>
    /// Limit firewall openings to specific configurations.
    /// If nothing is specified all defined configurations will be used.
    /// </summary>
    public DatabaseUsage DatabaseUsage { get; set; }

    /// <summary>
    /// Assure index on all statically defined collections.
    /// If this is false, the indexes will be checked the first time they are used.
    /// By default, this is true.
    /// </summary>
    public bool AssureIndex { get; set; } = true;

    /// <summary>
    /// Attach a logger on startup.
    /// </summary>
    public ILogger Logger { get; set; }
}