using System.Threading.Tasks;
using Tharga.Communication.MessageHandler;
using Tharga.MongoDB.Monitor.Client;

namespace Tharga.MongoDB.Monitor.Server;

/// <summary>
/// Receives an agent's <see cref="MonitorClientStatusMessage"/> and records its reported configuration
/// (call forwarding, queue interval, …) so it can be shown on the Clients page.
/// </summary>
public sealed class MonitorClientStatusHandler : PostMessageHandlerBase<MonitorClientStatusMessage>
{
    private readonly IDatabaseMonitor _databaseMonitor;

    public MonitorClientStatusHandler(IDatabaseMonitor databaseMonitor)
    {
        _databaseMonitor = databaseMonitor;
    }

    public override Task Handle(MonitorClientStatusMessage message)
    {
        _databaseMonitor.IngestClientStatus(message.SourceName, new MonitorClientStatus
        {
            ForwardCompletedCalls = message.ForwardCompletedCalls,
            QueueMetricIntervalMs = message.QueueMetricIntervalMs,
            StorageMode = message.StorageMode,
            EnableCommandMonitoring = message.EnableCommandMonitoring,
        }, ConnectionId);
        return Task.CompletedTask;
    }
}
