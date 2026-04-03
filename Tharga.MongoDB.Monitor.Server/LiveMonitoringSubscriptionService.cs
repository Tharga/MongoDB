using System;
using System.Threading.Tasks;
using Tharga.Communication.Server.Communication;
using Tharga.MongoDB.Monitor.Client;

namespace Tharga.MongoDB.Monitor.Server;

/// <summary>
/// Manages live monitoring subscriptions via Tharga.Communication.
/// When the first subscriber arrives, all connected agents are notified
/// and start sending queue metrics and ongoing calls.
/// </summary>
internal sealed class LiveMonitoringSubscriptionService : ILiveMonitoringSubscription
{
    private readonly IServerCommunication _serverCommunication;

    public LiveMonitoringSubscriptionService(IServerCommunication serverCommunication)
    {
        _serverCommunication = serverCommunication;
    }

    public Task<IAsyncDisposable> SubscribeAsync()
    {
        return _serverCommunication.SubscribeAsync<LiveMonitoringMarker>();
    }
}
