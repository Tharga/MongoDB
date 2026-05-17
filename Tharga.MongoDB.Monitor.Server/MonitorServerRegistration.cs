using System;
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
    /// Registers the MongoDB monitor server. Sets up a Tharga.Communication server with
    /// default client state tracking and the <see cref="MonitorCallHandler"/> family for
    /// receiving monitoring data from remote agents.
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    /// <param name="configure">Configures <see cref="MongoDbMonitorOptions"/> — accepted API keys, optional custom validator.</param>
    public static WebApplicationBuilder AddMongoDbMonitorServer(this WebApplicationBuilder builder, Action<MongoDbMonitorOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new MongoDbMonitorOptions();
        configure(options);

        // Pre-scan our assembly for handler types. AddThargaCommunicationServer's default
        // scan only covers the entry assembly's namespace, which won't include
        // MonitorCallHandler / MonitorCollectionInfoHandler / MonitorQueueMetricHandler
        // unless the host app happens to live in Tharga.MongoDB.Monitor.Server. Mirrors
        // the client-side pattern in MonitorClientRegistration.
        var ourHandlers = HandlerTypeService.GetHandlerTypes(
            builder.Services,
            [typeof(MonitorServerRegistration).Assembly]);

        builder.AddThargaCommunicationServer(commOptions =>
        {
            commOptions.RegisterClientStateService<MonitorClientStateService>();
            commOptions.RegisterClientRepository<MonitorClientRepository, MonitorClientConnectionInfo>();

            if (options.ApiKeys is { Length: > 0 })
                commOptions.ApiKeys = options.ApiKeys;

            options.ConfigureCommunicationOptions?.Invoke(commOptions);
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
    /// Legacy two-string overload. Kept for one release so existing consumers compile unchanged.
    /// Folds <paramref name="primaryApiKey"/> and <paramref name="secondaryApiKey"/> into the
    /// new <see cref="MongoDbMonitorOptions.ApiKeys"/> array — Tharga.Communication 0.2.0 removed
    /// the primary/secondary distinction; both are now just slots in a single accepted-keys array.
    /// </summary>
    [Obsolete("Use AddMongoDbMonitorServer(o => o.ApiKeys = [...]). Primary/secondary terminology was removed in Tharga.Communication 0.2.0.")]
    public static WebApplicationBuilder AddMongoDbMonitorServer(this WebApplicationBuilder builder, string primaryApiKey = null, string secondaryApiKey = null)
    {
        return builder.AddMongoDbMonitorServer(o =>
        {
            var keys = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrWhiteSpace(primaryApiKey)) keys.Add(primaryApiKey);
            if (!string.IsNullOrWhiteSpace(secondaryApiKey)) keys.Add(secondaryApiKey);
            if (keys.Count > 0) o.ApiKeys = keys.ToArray();
        });
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
