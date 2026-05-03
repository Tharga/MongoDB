namespace Tharga.MongoDB.Mcp;

/// <summary>
/// Options controlling the MCP surface exposed by <c>Tharga.MongoDB.Mcp</c>.
/// </summary>
public class MongoDbMcpOptions
{
    /// <summary>
    /// Maximum data-access level the MCP surface is allowed to expose.
    /// Default is <see cref="DataAccessLevel.Metadata"/>: only metadata and admin tools/resources are listed and callable.
    /// </summary>
    public DataAccessLevel DataAccess { get; set; } = DataAccessLevel.Metadata;
}
