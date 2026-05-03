namespace Tharga.MongoDB.Mcp;

/// <summary>
/// Controls how much MongoDB data the MCP surface exposes. Each level is a strict superset of the previous one.
/// </summary>
public enum DataAccessLevel
{
    /// <summary>
    /// Default. Only metadata and admin tools/resources are exposed — nothing that returns or modifies actual document data.
    /// </summary>
    Metadata = 0,

    /// <summary>
    /// Adds tools and resources that read actual document data (e.g. duplicate keys, explain plans containing filter values, recent/slow call payloads).
    /// </summary>
    DataRead = 1,

    /// <summary>
    /// Adds tools that modify document data (e.g. <c>mongodb.clean</c>).
    /// </summary>
    DataReadWrite = 2,
}
