using System.Threading.Tasks;
using Tharga.Communication.MessageHandler;
using Tharga.MongoDB.Monitor.Client;

namespace Tharga.MongoDB.Monitor.Server;

/// <summary>
/// Receives <see cref="MonitorCallMessage"/> from remote agents
/// and ingests the call data into the local <see cref="IDatabaseMonitor"/>.
/// </summary>
internal sealed class MonitorCallHandler : PostMessageHandlerBase<MonitorCallMessage>
{
    private readonly IDatabaseMonitor _databaseMonitor;

    public MonitorCallHandler(IDatabaseMonitor databaseMonitor)
    {
        _databaseMonitor = databaseMonitor;
    }

    public override Task Handle(MonitorCallMessage message)
    {
        _databaseMonitor.IngestCall(message.Call);
        return Task.CompletedTask;
    }
}
