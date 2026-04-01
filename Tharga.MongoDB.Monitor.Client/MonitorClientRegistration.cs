using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tharga.Communication.Client;

namespace Tharga.MongoDB.Monitor.Client;

/// <summary>
/// Extension methods for registering the MongoDB monitor client that forwards
/// monitoring data to a central server via Tharga.Communication.
/// </summary>
public static class MonitorClientRegistration
{
    /// <summary>
    /// Registers the MongoDB monitor client. Reads the server URL from
    /// <c>MonitorOptions.SendTo</c>. If <paramref name="sendTo"/> is provided
    /// it overrides the configured value.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="sendTo">The server URL to connect to.</param>
    /// <param name="apiKey">Optional API key for authenticating with the server.</param>
    public static IHostApplicationBuilder AddMongoDbMonitorClient(this IHostApplicationBuilder builder, string sendTo = null, string apiKey = null)
    {
        var serverAddress = sendTo;
        if (string.IsNullOrWhiteSpace(serverAddress)) return builder;

        builder.AddThargaCommunicationClient(o =>
        {
            o.ServerAddress = serverAddress;
            o.Pattern = MonitorConstants.DefaultHubPattern;
            if (!string.IsNullOrWhiteSpace(apiKey))
                o.ApiKey = apiKey;
        });

        builder.Services.AddHostedService<MonitorForwarder>();

        // Register action handlers for remote delegation
        builder.Services.AddTransient<TouchCollectionHandler>();
        builder.Services.AddTransient<DropIndexHandler>();
        builder.Services.AddTransient<RestoreIndexHandler>();
        builder.Services.AddTransient<CleanCollectionHandler>();

        return builder;
    }
}
