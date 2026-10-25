using System;
using System.Threading;

namespace Tharga.MongoDB.Tests.Support;

/// <summary>
/// MongoDB stores dates at millisecond precision, so a lock that expires now only reads as expired on the
/// server once the clock has moved into a later millisecond.
/// </summary>
internal static class MongoClock
{
    public static void WaitForNextMillisecond()
    {
        var current = CurrentMillisecond();
        SpinWait.SpinUntil(() => CurrentMillisecond() > current);
    }

    private static long CurrentMillisecond() => DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
}
