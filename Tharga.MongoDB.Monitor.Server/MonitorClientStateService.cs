using System;
using Microsoft.Extensions.Options;
using Tharga.Communication.Server;

namespace Tharga.MongoDB.Monitor.Server;

/// <summary>
/// Default client state service for monitor agent connections.
/// </summary>
internal sealed class MonitorClientStateService : ClientStateServiceBase<MonitorClientConnectionInfo>
{
    public MonitorClientStateService(IServiceProvider serviceProvider, IOptions<CommunicationOptions> options)
        : base(serviceProvider, options)
    {
    }

    protected override MonitorClientConnectionInfo Build(IClientConnectionInfo info)
    {
        return new MonitorClientConnectionInfo
        {
            Instance = info.Instance,
            ConnectionId = info.ConnectionId,
            Machine = info.Machine,
            Type = info.Type,
            Version = info.Version,
            IsConnected = info.IsConnected,
            ConnectTime = info.ConnectTime,
            DisconnectTime = info.DisconnectTime
        };
    }

    protected override MonitorClientConnectionInfo BuildDisconnect(MonitorClientConnectionInfo info, DateTime disconnectTime)
    {
        return info with { IsConnected = false, DisconnectTime = disconnectTime };
    }
}
