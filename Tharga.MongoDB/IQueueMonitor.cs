using System;
using System.Collections.Generic;

namespace Tharga.MongoDB;

public interface IQueueMonitor
{
    event EventHandler<QueueMetricEventArgs> QueueMetricEvent;
    IReadOnlyList<QueueMetricEventArgs> GetRecentMetrics();
    (int QueueCount, int ExecutingCount, double LastWaitTimeMs) GetCurrentState();
}
