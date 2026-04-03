using System.Threading.Tasks;
using Tharga.Communication.MessageHandler;
using Tharga.MongoDB.Monitor.Client;

namespace Tharga.MongoDB.Monitor.Server;

/// <summary>
/// Receives <see cref="MonitorCollectionInfoMessage"/> from remote agents
/// and ingests the collection metadata into the local <see cref="IDatabaseMonitor"/>.
/// </summary>
public sealed class MonitorCollectionInfoHandler : PostMessageHandlerBase<MonitorCollectionInfoMessage>
{
    private readonly IDatabaseMonitor _databaseMonitor;

    public MonitorCollectionInfoHandler(IDatabaseMonitor databaseMonitor)
    {
        _databaseMonitor = databaseMonitor;
    }

    public override Task Handle(MonitorCollectionInfoMessage message)
    {
        _databaseMonitor.IngestCollectionInfo(new RemoteCollectionInfoDto
        {
            ConfigurationName = message.ConfigurationName,
            DatabaseName = message.DatabaseName,
            CollectionName = message.CollectionName,
            SourceName = message.SourceName,
            Server = message.Server,
            DatabasePart = message.DatabasePart,
            Discovery = message.Discovery,
            Registration = message.Registration,
            EntityTypes = message.EntityTypes,
            Stats = message.Stats,
            Index = message.Index,
            Clean = message.Clean,
        }, ConnectionId);
        return Task.CompletedTask;
    }
}
