using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Tharga.Communication.Server;

namespace Tharga.MongoDB.Monitor.Server;

/// <summary>
/// Extension methods for registering the MongoDB monitor server that receives
/// monitoring data from remote agents via Tharga.Communication.
/// </summary>
public static class MonitorServerRegistration
{
    /// <summary>
    /// Registers the MongoDB monitor server. Sets up a Tharga.Communication server
    /// with default client state tracking and the <see cref="MonitorCallHandler"/>
    /// for receiving monitoring data from remote agents.
    /// </summary>
    public static WebApplicationBuilder AddMongoDbMonitorServer(this WebApplicationBuilder builder)
    {
        builder.AddThargaCommunicationServer(options =>
        {
            options.RegisterClientStateService<MonitorClientStateService>();
            options.RegisterClientRepository<MonitorClientRepository, MonitorClientConnectionInfo>();
        });

        // Ensure the handler is registered even if assembly scanning misses it
        builder.Services.AddTransient<MonitorCallHandler>();

        return builder;
    }

    /// <summary>
    /// Maps the SignalR hub endpoint for receiving monitoring data from remote agents.
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <param name="pattern">The hub URL pattern. Defaults to <c>"hub"</c>.</param>
    public static WebApplication UseMongoDbMonitorServer(this WebApplication app, string pattern = null)
    {
        app.UseThargaCommunicationServer(pattern);
        return app;
    }
}
