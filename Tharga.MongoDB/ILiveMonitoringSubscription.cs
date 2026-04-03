using System;
using System.Threading.Tasks;

namespace Tharga.MongoDB;

/// <summary>
/// Manages live monitoring subscriptions. When subscribers are present,
/// remote agents send queue metrics and ongoing call data.
/// Implemented by Monitor.Server, no-op when not installed.
/// </summary>
public interface ILiveMonitoringSubscription
{
    Task<IAsyncDisposable> SubscribeAsync();
}
