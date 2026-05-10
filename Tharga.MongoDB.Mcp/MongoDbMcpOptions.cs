using Tharga.MongoDB.Configuration;

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

    /// <summary>
    /// Optional MongoDB Atlas Administration API access. When set, the package registers a separate
    /// MCP tool provider with read-only Atlas tools (<c>atlas.list_clusters</c>,
    /// <c>atlas.get_performance_advisor_suggestions</c>, <c>atlas.get_open_alerts</c>) on the System scope.
    /// Leave null to disable — the Atlas tool surface is opt-in.
    /// </summary>
    public MongoDbApiAccess Atlas { get; set; }
}
