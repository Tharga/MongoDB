using System;

namespace Tharga.MongoDB;

/// <summary>
/// Thrown by <see cref="MongoDbRegistrationExtensions.UseMongoDB"/> when one or more configured
/// connections are unreachable at startup and <see cref="Configuration.StartupConnectivityMode.FailFast"/>
/// is in effect. Unlike the previous unhandled <c>TimeoutException</c>, this is thrown only after
/// the failure has been logged (<c>LogCritical</c>) and
/// <see cref="Configuration.UseMongoOptions.StartupFailureCallback"/> has been awaited — so it is
/// observable in telemetry before the process exits.
/// </summary>
public sealed class MongoStartupConnectivityException : Exception
{
    internal MongoStartupConnectivityException(MongoStartupFailure failure)
        : base($"MongoDB startup connectivity check failed for {failure.UnreachableConnections.Count} configuration(s): {failure.Summary}")
    {
        Failure = failure;
    }

    /// <summary>
    /// Details of the connections that could not be reached.
    /// </summary>
    public MongoStartupFailure Failure { get; }
}
