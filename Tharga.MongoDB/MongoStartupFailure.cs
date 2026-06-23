using System.Collections.Generic;
using System.Linq;

namespace Tharga.MongoDB;

/// <summary>
/// Describes the set of configured connections that were unreachable during the startup
/// connectivity pre-check. Passed to <see cref="Configuration.UseMongoOptions.StartupFailureCallback"/>
/// and carried by <see cref="MongoStartupConnectivityException"/>.
/// </summary>
public record MongoStartupFailure
{
    internal MongoStartupFailure(IEnumerable<ConnectionConnectivity> unreachableConnections)
    {
        UnreachableConnections = (unreachableConnections ?? Enumerable.Empty<ConnectionConnectivity>()).ToArray();
    }

    /// <summary>
    /// The connections that could not be reached, including the failure message for each.
    /// </summary>
    public IReadOnlyList<ConnectionConnectivity> UnreachableConnections { get; }

    /// <summary>
    /// A single-line summary listing the unreachable configuration names and their messages.
    /// </summary>
    public string Summary => string.Join("; ", UnreachableConnections.Select(c => $"{c.ConfigurationName}: {c.Message}"));
}
