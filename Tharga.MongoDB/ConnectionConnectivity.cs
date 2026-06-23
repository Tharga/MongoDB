using System;

namespace Tharga.MongoDB;

/// <summary>
/// The connectivity result for a single configured connection, as produced by
/// <see cref="IMongoDbConnectivityState"/>. Built on the same non-throwing probe as
/// <see cref="IMongoDbService.GetInfoAsync"/> / <see cref="DatabaseInfo.CanConnect"/>.
/// </summary>
public record ConnectionConnectivity
{
    internal ConnectionConnectivity()
    {
    }

    /// <summary>
    /// The configuration name this result belongs to.
    /// </summary>
    public string ConfigurationName { get; init; }

    /// <summary>
    /// True when the connection could be reached (after assuring the firewall, when requested).
    /// </summary>
    public bool CanConnect { get; init; }

    /// <summary>
    /// A human-readable database description when reachable, or the failure message when not.
    /// </summary>
    public string Message { get; init; }

    /// <summary>
    /// The firewall-assurance message produced while probing, when available.
    /// </summary>
    public string Firewall { get; init; }

    /// <summary>
    /// When (UTC) this result was produced.
    /// </summary>
    public DateTime CheckedAt { get; init; }
}
