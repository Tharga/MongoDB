using System;

namespace Tharga.MongoDB;

internal record AggregatedCallInfo
{
    public int Count;
    public TimeSpan TotalElapsed;
    public TimeSpan MaxElapsed;

    public TimeSpan Average => TimeSpan.FromTicks(TotalElapsed.Ticks / Count);
}