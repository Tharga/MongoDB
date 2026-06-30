using System.Reflection;

namespace Tharga.MongoDB.Monitor.Server;

/// <summary>
/// Default <see cref="IMonitorServerInfo"/> that reports this <c>Tharga.MongoDB.Monitor.Server</c>
/// assembly's library version. Registered by <see cref="MonitorServerRegistration.AddMongoDbMonitorServer(Microsoft.AspNetCore.Builder.WebApplicationBuilder, System.Action{MongoDbMonitorOptions})"/>.
/// </summary>
internal sealed class MonitorServerInfo : IMonitorServerInfo
{
    public string LibraryVersion { get; } = typeof(MonitorServerInfo).Assembly.GetLibraryVersion();
}
