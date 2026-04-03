using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tharga.Communication.Client;
using Tharga.Communication.MessageHandler;

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

        // Pre-scan our assembly for handler types. The default scan inside
        // AddThargaCommunicationClient only covers the entry assembly's namespace,
        // which may not include Tharga.MongoDB.Monitor.Client handlers.
        var ourHandlers = HandlerTypeService.GetHandlerTypes(
            builder.Services,
            [typeof(MonitorClientRegistration).Assembly]);

        builder.AddThargaCommunicationClient(o =>
        {
            o.ServerAddress = serverAddress;
            o.Pattern = MonitorConstants.DefaultHubPattern;
            if (!string.IsNullOrWhiteSpace(apiKey))
                o.ApiKey = apiKey;
        });

        // Replace the handler type service with a merged one that includes our handlers.
        // AddThargaCommunicationClient registered its own IHandlerTypeService — we override it.
        var defaultHandlers = HandlerTypeService.GetHandlerTypes(builder.Services);
        foreach (var kvp in ourHandlers)
            defaultHandlers.TryAdd(kvp.Key, kvp.Value);

        // Remove existing registration and add merged one
        var existing = builder.Services.FirstOrDefault(d => d.ServiceType == typeof(IHandlerTypeService));
        if (existing != null) builder.Services.Remove(existing);
        builder.Services.AddSingleton<IHandlerTypeService>(new HandlerTypeService(defaultHandlers));

        builder.Services.AddHostedService<MonitorForwarder>();

        return builder;
    }
}
