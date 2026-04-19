using Tharga.Mcp;

namespace Tharga.MongoDB.Mcp;

/// <summary>
/// Extension methods for <see cref="IThargaMcpBuilder"/> that register Tharga.MongoDB MCP providers.
/// </summary>
public static class ThargaMcpBuilderExtensions
{
    /// <summary>
    /// Registers <see cref="MongoDbResourceProvider"/> and <see cref="MongoDbToolProvider"/>, exposing
    /// MongoDB collection data, monitoring data, and admin tools on the System scope.
    /// </summary>
    public static IThargaMcpBuilder AddMongoDB(this IThargaMcpBuilder builder)
    {
        builder.AddResourceProvider<MongoDbResourceProvider>();
        builder.AddToolProvider<MongoDbToolProvider>();
        return builder;
    }
}
