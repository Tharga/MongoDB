using System;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Tharga.MongoDB.Configuration;
using Tharga.MongoDB.Disk;
using Xunit;

namespace Tharga.MongoDB.Tests;

/// <summary>
/// The per-call stores must stay bounded by <c>LastCallsToKeep</c> whether or not anyone reads the
/// monitor. A headless service with monitoring enabled never calls <c>GetOngoingCalls</c>, so any store
/// that is only pruned on the read path grows for the life of the process. See GitHub issue #148.
/// </summary>
public class CallLibraryCapTests
{
    private const int Cap = 10;

    private static CallLibrary NewLibrary()
    {
        return new CallLibrary(Options.Create(new DatabaseOptions { Monitor = new MonitorOptions { LastCallsToKeep = Cap } }));
    }

    private static CollectionFingerprint Fingerprint()
    {
        return new CollectionFingerprint
        {
            ConfigurationName = (ConfigurationName)"A",
            DatabaseName = "Db",
            CollectionName = "Coll",
        };
    }

    private static Guid RunCall(CallLibrary sut)
    {
        var key = Guid.NewGuid();
        sut.StartCall(new CallStartEventArgs(key, Fingerprint(), "fn", Operation.Read));
        sut.EndCall(new CallEndEventArgs(key, TimeSpan.FromMilliseconds(1), null, 1));
        return key;
    }

    [Fact]
    public void StartCall_ManyCompletedCallsAndNobodyReading_KeepsCompletionStampsCapped()
    {
        using var sut = NewLibrary();

        // Deliberately never call GetOngoingCalls — that read path used to be the only pruner.
        for (var i = 0; i < Cap * 100; i++) RunCall(sut);

        sut.CompletedStampCount.Should().BeLessThanOrEqualTo(Cap);
        sut.GetLastCalls().Should().HaveCount(Cap);
    }

    [Fact]
    public void IngestCall_ManyCompletedCallsAndNobodyReading_KeepsCompletionStampsCapped()
    {
        using var sut = NewLibrary();

        for (var i = 0; i < Cap * 100; i++)
        {
            sut.IngestCall(new CallInfo
            {
                Key = Guid.NewGuid(),
                SourceName = "src",
                StartTime = DateTime.UtcNow,
                Fingerprint = Fingerprint(),
                FunctionName = "fn",
                Operation = Operation.Read,
                Elapsed = TimeSpan.FromMilliseconds(1),
                Final = true,
            });
        }

        sut.CompletedStampCount.Should().BeLessThanOrEqualTo(Cap);
    }

    [Fact]
    public void EndCall_ForCallEvictedWhileStillRunning_StillClearsTheInFlightEntry()
    {
        using var sut = NewLibrary();

        // A slow call starts, then a flood of short calls pushes it out of the capped recent ring
        // while it is still running.
        var slowKey = Guid.NewGuid();
        sut.StartCall(new CallStartEventArgs(slowKey, Fingerprint(), "slow", Operation.Read));
        for (var i = 0; i < Cap * 5; i++) RunCall(sut);

        sut.GetOngoingCalls().Should().Contain(x => x.Key == slowKey, "it is still running");

        sut.EndCall(new CallEndEventArgs(slowKey, TimeSpan.FromSeconds(30), null, 1));

        sut.InFlightCount.Should().Be(0);
        sut.GetOngoingCalls().Should().NotContain(x => x.Key == slowKey, "it has finished");
    }

    [Fact]
    public void GetOngoingCalls_AfterTheCapPrunes_StillReportsCallsThatAreStillRunning()
    {
        using var sut = NewLibrary();

        var running = Enumerable.Range(0, 3).Select(_ =>
        {
            var key = Guid.NewGuid();
            sut.StartCall(new CallStartEventArgs(key, Fingerprint(), "slow", Operation.Read));
            return key;
        }).ToArray();

        for (var i = 0; i < Cap * 5; i++) RunCall(sut);

        sut.GetOngoingCalls().Select(x => x.Key).Should().Contain(running);
    }
}
