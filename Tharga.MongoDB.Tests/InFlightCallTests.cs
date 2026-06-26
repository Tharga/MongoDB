using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Tharga.MongoDB.Configuration;
using Tharga.MongoDB.Disk;
using Xunit;

namespace Tharga.MongoDB.Tests;

public class InFlightCallTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static ExecuteLimiter CreateLimiter() =>
        new(Mock.Of<IOptions<ExecuteLimiterOptions>>(x =>
                x.Value == new ExecuteLimiterOptions { Enabled = true }),
            NullLogger<ExecuteLimiter>.Instance);

    private static ExecuteCallContext Ctx(string function, Operation operation, string filterJson = null) => new()
    {
        CallKey = Guid.NewGuid(),
        ConfigurationName = "cfg",
        DatabaseName = "db",
        CollectionName = "Things",
        FunctionName = function,
        Operation = operation,
        FilterProvider = filterJson == null ? null : () => filterJson,
    };

    [Fact]
    public async Task GetInFlightCalls_DistinguishesQueuedFromExecuting_WithMetadataAndRenderedFilter()
    {
        var limiter = CreateLimiter();
        var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Pool size of 1 -> a single concurrency slot, so the second call must queue behind the first.
        // Occupy the only slot; this one is executing.
        var executing = limiter.ExecuteAsync(async _ => { await gate.Task; return 0; },
            "srv", 1, Ctx("GetManyAsync", Operation.Read, "{ \"x\" : 1 }"), CancellationToken.None);
        await SpinUntil(() => limiter.GetInFlightCalls().Any(c => c.IsExecuting));

        // This one cannot acquire the slot; it stays queued.
        var queued = limiter.ExecuteAsync(_ => Task.FromResult(1),
            "srv", 1, Ctx("UpdateOneAsync", Operation.Update), CancellationToken.None);
        await SpinUntil(() => limiter.GetInFlightCalls().Count(c => !c.IsExecuting) == 1);

        var inFlight = limiter.GetInFlightCalls();
        inFlight.Should().HaveCount(2);

        var exec = inFlight.Single(c => c.IsExecuting);
        exec.FunctionName.Should().Be("GetManyAsync");
        exec.CollectionName.Should().Be("Things");
        exec.Filter.Should().Be("{ \"x\" : 1 }"); // rendered on inspection

        var wait = inFlight.Single(c => !c.IsExecuting);
        wait.FunctionName.Should().Be("UpdateOneAsync");
        wait.Operation.Should().Be(Operation.Update);

        gate.SetResult(0);
        await Task.WhenAll(executing, queued).WaitAsync(Timeout);

        await SpinUntil(() => limiter.GetInFlightCalls().Count == 0); // drained when calls finish
    }

    [Fact]
    public void OngoingCalls_AreNotEvicted_WhenRecentRingOverflows()
    {
        var library = new CallLibrary(Options.Create(new DatabaseOptions
        {
            Monitor = new MonitorOptions { LastCallsToKeep = 2 },
        }));

        // Three concurrent (never-finalized) calls, but the recent ring only keeps 2.
        for (var i = 0; i < 3; i++)
        {
            var fingerprint = new CollectionFingerprint { ConfigurationName = "cfg", DatabaseName = "db", CollectionName = $"Coll{i}" };
            library.StartCall(new CallStartEventArgs(Guid.NewGuid(), fingerprint, "GetManyAsync", Operation.Read, "src"));
        }

        // All three are still in flight, so all three must show as ongoing despite the ring cap of 2.
        library.GetOngoingCalls().Count(c => !c.Final).Should().Be(3);
    }

    private static async Task SpinUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Condition not met within timeout.");
            await Task.Delay(5);
        }
    }
}
