using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Tharga.Communication.MessageHandler;

namespace Tharga.MongoDB.Monitor.Client;

/// <summary>
/// Receives <see cref="SetLiveMonitoringActiveMessage"/> from the central server and flips the local
/// <see cref="LiveMonitoringState"/> flag that <see cref="MonitorForwarder"/> checks before forwarding
/// queue metrics. Logs every receipt so the live-monitoring signal is visible in the agent log.
/// Resolves <see cref="LiveMonitoringState"/> via the service provider (it is internal, so it cannot
/// appear in this public handler's constructor signature) — mirrors <see cref="ResetCacheHandler"/>.
/// </summary>
public sealed class SetLiveMonitoringActiveHandler : PostMessageHandlerBase<SetLiveMonitoringActiveMessage>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SetLiveMonitoringActiveHandler> _logger;

    public SetLiveMonitoringActiveHandler(IServiceProvider serviceProvider, ILogger<SetLiveMonitoringActiveHandler> logger = null)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public override Task Handle(SetLiveMonitoringActiveMessage message)
    {
        _logger?.LogDebug("Received SetLiveMonitoringActive(Active={Active}) from the monitor server.", message.Active);

        if (_serviceProvider.GetService(typeof(LiveMonitoringState)) is LiveMonitoringState state)
            state.Active = message.Active;

        return Task.CompletedTask;
    }
}
