using Tharga.Communication.Server;

namespace Tharga.MongoDB.Monitor.Server;

/// <summary>
/// Default in-memory repository for monitor agent connections.
/// </summary>
internal sealed class MonitorClientRepository : MemoryClientRepository<MonitorClientConnectionInfo>
{
}
