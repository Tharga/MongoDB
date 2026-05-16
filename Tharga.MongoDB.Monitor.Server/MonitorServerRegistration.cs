using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Tharga.Communication.MessageHandler;
using Tharga.Communication.Server;
using Tharga.MongoDB.Monitor.Client;

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
    /// <param name="builder">The web application builder.</param>
    /// <param name="primaryApiKey">Optional primary API key. When set, clients must provide a matching key to connect.</param>
    /// <param name="secondaryApiKey">Optional secondary API key for zero-downtime key rotation.</param>
    public static WebApplicationBuilder AddMongoDbMonitorServer(this WebApplicationBuilder builder, string primaryApiKey = null, string
secondaryApiKey = null)
    {
        // Pre-scan our assembly for handler types. AddThargaCommunicationServer's default
        // scan only covers the entry assembly's namespace, which won't include
        // MonitorCallHandler / MonitorCollectionInfoHandler / MonitorQueueMetricHandler
        // unless the host app happens to live in Tharga.MongoDB.Monitor.Server. Mirrors
        // the client-side pattern in MonitorClientRegistration.
        var ourHandlers = HandlerTypeService.GetHandlerTypes(
            builder.Services,
            [typeof(MonitorServerRegistration).Assembly]);

        builder.AddThargaCommunicationServer(options =>
        {
            options.RegisterClientStateService<MonitorClientStateService>();
            options.RegisterClientRepository<MonitorClientRepository, MonitorClientConnectionInfo>();
            if (!string.IsNullOrWhiteSpace(primaryApiKey))
                options.PrimaryApiKey = primaryApiKey;
            if (!string.IsNullOrWhiteSpace(secondaryApiKey))
                options.SecondaryApiKey = secondaryApiKey;
        });

        // Merge our handlers into the IHandlerTypeService AddThargaCommunicationServer
        // registered. Without this, MonitorCallMessage / MonitorCollectionInfoMessage /
        // MonitorQueueMetricMessage arrive at the hub but the dispatcher can't resolve
        // a handler and silently drops them — the symptom is "agent connects but no
        // remote calls or collections ever appear in CallView / CollectionView".
        var defaultHandlers = HandlerTypeService.GetHandlerTypes(builder.Services);
        foreach (var kvp in ourHandlers)
            defaultHandlers.TryAdd(kvp.Key, kvp.Value);

        var existing = builder.Services.FirstOrDefault(d => d.ServiceType == typeof(IHandlerTypeService));
        if (existing != null) builder.Services.Remove(existing);
        builder.Services.AddSingleton<IHandlerTypeService>(new HandlerTypeService(defaultHandlers));

        // Register handler concrete types in DI so the dispatcher can resolve them.
        builder.Services.AddTransient<MonitorCallHandler>();
        builder.Services.AddTransient<MonitorCollectionInfoHandler>();
        builder.Services.AddTransient<MonitorQueueMetricHandler>();

        // Bridge client connection events into IDatabaseMonitor
        builder.Services.AddHostedService<MonitorClientBridge>();

        // Enable remote action delegation
        builder.Services.AddSingleton<IRemoteActionDispatcher, RemoteActionDispatcher>();

        // Enable subscription-based live monitoring
        builder.Services.AddSingleton<ILiveMonitoringSubscription, LiveMonitoringSubscriptionService>();

        return builder;
    }

    /// <summary>
    /// Maps the SignalR hub endpoint for receiving monitoring data from remote agents.
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <param name="pattern">The hub URL pattern. Defaults to <c>"hub"</c>.</param>
    public static WebApplication UseMongoDbMonitorServer(this WebApplication app, string pattern = null)
    {
        app.UseThargaCommunicationServer(pattern ?? MonitorConstants.DefaultHubPattern);
        return app;
    }
}