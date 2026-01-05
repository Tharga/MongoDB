using Microsoft.Extensions.Logging;

namespace Tharga.MongoDB.Configuration;

public record UseMongoOptions
{
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