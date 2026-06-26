using System.Threading.Tasks;
using Tharga.Communication.MessageHandler;
using Tharga.MongoDB.Monitor.Client;

namespace Tharga.MongoDB.Monitor.Server;

/// <summary>
/// Receives <see cref="MonitorCollectionDroppedMessage"/> from remote agents and removes the
/// agent's claim on the dropped collection from the local <see cref="IDatabaseMonitor"/>.
/// </summary>
public sealed class MonitorCollectionDroppedHandler : PostMessageHandlerBase<MonitorCollectionDroppedMessage>
{
    private readonly IDatabaseMonitor _databaseMonitor;

    public MonitorCollectionDroppedHandler(IDatabaseMonitor databaseMonitor)
    {
        _databaseMonitor = databaseMonitor;
    }

    public override Task Handle(MonitorCollectionDroppedMessage message)
    {
        _databaseMonitor.IngestCollectionDropped(message.SourceName, message.ConfigurationName, message.DatabaseName, message.CollectionName);
        return Task.CompletedTask;
    }
}
