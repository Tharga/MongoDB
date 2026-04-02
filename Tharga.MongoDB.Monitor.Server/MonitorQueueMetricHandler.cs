using System.Threading.Tasks;
using Tharga.Communication.MessageHandler;
using Tharga.MongoDB.Monitor.Client;

namespace Tharga.MongoDB.Monitor.Server;

/// <summary>
/// Receives <see cref="MonitorQueueMetricMessage"/> from remote agents
/// and ingests the queue state into the local <see cref="IDatabaseMonitor"/>.
/// </summary>
public sealed class MonitorQueueMetricHandler : PostMessageHandlerBase<MonitorQueueMetricMessage>
{
    private readonly IDatabaseMonitor _databaseMonitor;

    public MonitorQueueMetricHandler(IDatabaseMonitor databaseMonitor)
    {
        _databaseMonitor = databaseMonitor;
    }

    public override Task Handle(MonitorQueueMetricMessage message)
    {
        _databaseMonitor.IngestQueueMetric(message.SourceName, message.QueueCount, message.ExecutingCount, message.WaitTimeMs);
        return Task.CompletedTask;
    }
}
