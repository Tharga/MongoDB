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
    public static IHostApplicationBuilder AddMongoDbMonitorClient(this IHostApplicationBuilder builder, string sendTo = null)
    {
        var serverAddress = sendTo;
        if (string.IsNullOrWhiteSpace(serverAddress)) return builder;

        builder.AddThargaCommunicationClient(o =>
        {
            o.ServerAddress = serverAddress;
        });

        builder.Services.AddHostedService<MonitorForwarder>();

        return builder;
    }
}
