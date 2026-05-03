using System;
using Microsoft.Extensions.DependencyInjection;
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
    /// <param name="builder">The MCP builder.</param>
    /// <param name="configure">Optional callback to configure <see cref="MongoDbMcpOptions"/>. If omitted, defaults are used (<see cref="DataAccessLevel.Metadata"/>).</param>
    public static IThargaMcpBuilder AddMongoDB(this IThargaMcpBuilder builder, Action<MongoDbMcpOptions> configure = null)
    {
        var options = new MongoDbMcpOptions();
        configure?.Invoke(options);
        builder.Services.AddSingleton(options);

        builder.AddResourceProvider<MongoDbResourceProvider>();
        builder.AddToolProvider<MongoDbToolProvider>();
        return builder;
    }
}
