using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<LiveMonitoringSubscriptionService> _logger;

    public LiveMonitoringSubscriptionService(IServerCommunication serverCommunication, IDatabaseMonitor databaseMonitor, ILogger<LiveMonitoringSubscriptionService> logger = null)
    {
        _serverCommunication = serverCommunication;
        _databaseMonitor = databaseMonitor;
        _logger = logger;
    }

    public async Task<IAsyncDisposable> SubscribeAsync()
    {
        var inner = await _serverCommunication.SubscribeAsync<LiveMonitoringMarker>();

        var counts = _serverCommunication.GetSubscriptions();
        var topicCount = counts.TryGetValue(LiveTopic, out var c) ? c : -1;
        _logger?.LogDebug("Live monitoring SubscribeAsync: topic {Topic} now has {Count} subscriber(s) on the server. Notifying agents.", LiveTopic, topicCount);

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
        var connectedClients = _databaseMonitor.GetMonitorClients().Where(x => x.IsConnected).ToArray();
        var sources = string.Join(", ", connectedClients.Select(x => x.SourceName ?? "(no source)"));
        _logger?.LogDebug("Broadcasting live-monitoring state (Active={HasSubscribers}) to agents via PostToAll. Connected agents: {Count} [{Sources}].",
            hasSubscribers, connectedClients.Length, sources);

        try
        {
            // Explicit, fully server-controlled signal — the reliable path. The agent has a dedicated
            // handler (SetLiveMonitoringActiveHandler) that flips its local flag and logs receipt.
            await _serverCommunication.PostToAllAsync(new SetLiveMonitoringActiveMessage
            {
                Active = hasSubscribers,
            });

            // Also send the framework's SubscriptionStateChanged for back-compat with the built-in
            // HasSubscribers tracker (kept as a secondary signal; the explicit message above is primary).
            await _serverCommunication.PostToAllAsync(new SubscriptionStateChanged
            {
                Topic = LiveTopic,
                Key = null,
                HasSubscribers = hasSubscribers,
            });

            _logger?.LogDebug("PostToAll SetLiveMonitoringActive(Active={HasSubscribers}) + SubscriptionStateChanged completed.", hasSubscribers);

            // Surface it in each connected agent's Communication log.
            foreach (var client in connectedClients)
                if (!string.IsNullOrEmpty(client.SourceName))
                    _databaseMonitor.RecordClientCommunication(client.SourceName, CommunicationDirection.Outbound,
                        "SubscriptionState", $"LiveMonitoring hasSubscribers={hasSubscribers}");
        }
        catch (Exception ex)
        {
            // Best-effort notification; never let it break subscribe/unsubscribe — but make the failure visible.
            _logger?.LogWarning(ex, "Failed to broadcast SubscriptionStateChanged(HasSubscribers={HasSubscribers}) to agents.", hasSubscribers);
        }
    }

    private async Task OnSubscriptionDisposedAsync()
    {
        // Tell agents to stop only when the last subscriber has actually gone (use the real count).
        var remaining = _serverCommunication.GetSubscriptions().TryGetValue(LiveTopic, out var count) ? count : 0;
        _logger?.LogDebug("Live monitoring subscription disposed: topic {Topic} now has {Count} subscriber(s) remaining.", LiveTopic, remaining);
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
