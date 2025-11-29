using System;

namespace Tharga.MongoDB.Configuration;

public record MonitorOptions
{
    public bool Enabled { get; set; } = true;
    public int LastCallsToKeep { get; set; } = 1000;
    public TimeSpan SlowCallThreshold { get; set; } = TimeSpan.FromMicroseconds(1000);
    public int SlowCallsToKeep { get; set; } = 200;
}