using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Tharga.Communication.Contract;
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
    private static readonly string LiveTopic = typeof(LiveMonitoringMarker).FullName;

    private readonly IServerCommunication _serverCommunication;
    private readonly IDatabaseMonitor _databaseMonitor;

    public LiveMonitoringSubscriptionService(IServerCommunication serverCommunication, IDatabaseMonitor databaseMonitor)
    {
        _serverCommunication = serverCommunication;
        _databaseMonitor = databaseMonitor;
    }

    public async Task<IAsyncDisposable> SubscribeAsync()
    {
        var inner = await _serverCommunication.SubscribeAsync<LiveMonitoringMarker>();

        // Explicitly tell all connected agents a subscriber is active. The framework auto-pushes this
        // on the 0<->1 boundary, but that hasn't been reaching agents (they never start sending queue
        // metrics) — so drive it ourselves over the known-good PostToAll path. Idempotent, so safe to
        // send on every subscribe.
        await BroadcastAsync(true);

        return new Handle(inner, this);
    }

    public IReadOnlyDictionary<string, int> GetSubscriptions()
    {
        return _serverCommunication.GetSubscriptions();
    }

    private async Task BroadcastAsync(bool hasSubscribers)
    {
        try
        {
            await _serverCommunication.PostToAllAsync(new SubscriptionStateChanged
            {
                Topic = LiveTopic,
                Key = null,
                HasSubscribers = hasSubscribers,
            });

            // Surface it in each connected agent's Communication log.
            foreach (var client in _databaseMonitor.GetMonitorClients())
                if (client.IsConnected && !string.IsNullOrEmpty(client.SourceName))
                    _databaseMonitor.RecordClientCommunication(client.SourceName, CommunicationDirection.Outbound,
                        "SubscriptionState", $"LiveMonitoring hasSubscribers={hasSubscribers}");
        }
        catch
        {
            // Best-effort notification; never let it break subscribe/unsubscribe.
        }
    }

    private async Task OnSubscriptionDisposedAsync()
    {
        // Tell agents to stop only when the last subscriber has actually gone (use the real count).
        var remaining = _serverCommunication.GetSubscriptions().TryGetValue(LiveTopic, out var count) ? count : 0;
        if (remaining <= 0)
            await BroadcastAsync(false);
    }

    private sealed class Handle : IAsyncDisposable
    {
        private readonly IAsyncDisposable _inner;
        private readonly LiveMonitoringSubscriptionService _owner;

        public Handle(IAsyncDisposable inner, LiveMonitoringSubscriptionService owner)
        {
            _inner = inner;
            _owner = owner;
        }

        public async ValueTask DisposeAsync()
        {
            await _inner.DisposeAsync();
            await _owner.OnSubscriptionDisposedAsync();
        }
    }
}
