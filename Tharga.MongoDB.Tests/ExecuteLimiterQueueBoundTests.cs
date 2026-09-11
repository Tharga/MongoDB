using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Tharga.MongoDB.Disk;
using Tharga.MongoDB.Internals;
using Xunit;

namespace Tharga.MongoDB.Tests;

/// <summary>
/// Coverage for GitHub issue #155. The wait queue was unbounded, so a burst of work faster than the pool
/// drains became a process-killing memory event rather than a throttling one — ~11,900 queued operations and
/// a ~6 GB heap in the reported incident.
/// </summary>
public class ExecuteLimiterQueueBoundTests
{
    private const string ServerKey = "localhost:27017|pool=1";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static ExecuteLimiter CreateLimiter(int? maxQueueLength = null, TimeSpan? queueTimeout = null, ILogger<ExecuteLimiter> logger = null)
    {
        var options = new ExecuteLimiterOptions { Enabled = true, MaxQueueLength = maxQueueLength, QueueTimeout = queueTimeout };
        return new ExecuteLimiter(Mock.Of<IOptions<ExecuteLimiterOptions>>(x => x.Value == options), logger ?? NullLogger<ExecuteLimiter>.Instance);
    }

    private static ExecuteCallContext Ctx() => new()
    {
        CallKey = Guid.NewGuid(),
        ConfigurationName = "Default",
        FunctionName = "test",
        Operation = Operation.Read,
    };

    private static Task<(bool Result, ExecuteInfo Info)> Execute(ExecuteLimiter limiter, Func<CancellationToken, Task<bool>> action, int poolSize = 1)
    {
        return limiter.ExecuteAsync(action, ServerKey, poolSize, Ctx(), CancellationToken.None);
    }

    /// <summary>
    /// Occupies the single pool slot until released, so the pool is genuinely saturated and the next caller
    /// has to queue. Returns once the blocker is known to be executing.
    /// </summary>
    private static async Task<(Task Running, TaskCompletionSource Release)> Saturate(ExecuteLimiter limiter)
    {
        var executing = new TaskCompletionSource();
        var release = new TaskCompletionSource();

        var running = Execute(limiter, async _ =>
        {
            executing.SetResult();
            await release.Task;
            return true;
        });

        await executing.Task.WaitAsync(Timeout);
        return (running, release);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Condition was not met in time.");
            await Task.Delay(10);
        }
    }

    private static PoolQueueState PoolState(ExecuteLimiter limiter) => limiter.GetPerPoolState().Single(x => x.ServerKey == ServerKey);

    // --- The correction: the bound is on waiters, not on everything transiting the admission path ---

    [Fact]
    public async Task AnIdlePool_ExecutesEvenWhenQueueingIsForbidden()
    {
        // MaxQueueLength = 0 means "never queue", not "never run". Capping the queued counter instead — which
        // every caller increments before touching the semaphore — would reject this call against a free pool.
        var limiter = CreateLimiter(maxQueueLength: 0);

        var (result, _) = await Execute(limiter, _ => Task.FromResult(true));

        result.Should().BeTrue();
    }

    [Fact]
    public async Task AFreeSlot_IsNeverCountedAgainstTheLimit()
    {
        // Ten sequential calls on a pool of one: each finds the slot free, so none of them is ever a waiter.
        var limiter = CreateLimiter(maxQueueLength: 0);

        for (var i = 0; i < 10; i++)
        {
            var (result, _) = await Execute(limiter, _ => Task.FromResult(true));
            result.Should().BeTrue();
        }

        PoolState(limiter).RejectedCount.Should().Be(0);
    }

    [Fact]
    public async Task ConcurrentCallers_WithSlotsFree_AreNotRejected()
    {
        // Pool of 4, cap of 0, four simultaneous callers. Capping the queued counter would reject three of
        // them despite every slot being free.
        var limiter = CreateLimiter(maxQueueLength: 0);
        var gate = new TaskCompletionSource();
        var running = Enumerable.Range(0, 4)
            .Select(_ => Execute(limiter, async _ => { await gate.Task; return true; }, poolSize: 4))
            .ToArray();

        await WaitUntil(() => PoolState(limiter).ExecutingCount == 4);
        gate.SetResult();
        await Task.WhenAll(running).WaitAsync(Timeout);

        running.Should().OnlyContain(x => x.Result.Result);
        PoolState(limiter).RejectedCount.Should().Be(0);
    }

    // --- The bound itself ---

    [Fact]
    public async Task WithTheQueueFull_TheNextCallerIsRejected()
    {
        var limiter = CreateLimiter(maxQueueLength: 0);
        var (running, release) = await Saturate(limiter);

        var act = () => Execute(limiter, _ => Task.FromResult(true));

        var exception = await act.Should().ThrowAsync<ExecuteLimiterQueueFullException>();
        exception.Which.ServerKey.Should().Be(ServerKey);
        exception.Which.MaxQueueLength.Should().Be(0);

        release.SetResult();
        await running.WaitAsync(Timeout);
    }

    [Fact]
    public async Task TheAllowedWaitersQueue_AndOnlyTheOneBeyondTheLimitIsRejected()
    {
        var limiter = CreateLimiter(maxQueueLength: 2);
        var (running, release) = await Saturate(limiter);

        var waiters = Enumerable.Range(0, 2).Select(_ => Execute(limiter, _ => Task.FromResult(true))).ToArray();
        await WaitUntil(() => PoolState(limiter).QueueCount >= 2);

        var act = () => Execute(limiter, _ => Task.FromResult(true));
        await act.Should().ThrowAsync<ExecuteLimiterQueueFullException>();

        release.SetResult();
        await running.WaitAsync(Timeout);
        await Task.WhenAll(waiters).WaitAsync(Timeout);
        waiters.Should().OnlyContain(x => x.Result.Result);
    }

    [Fact]
    public async Task ARejectedCall_LeavesNoTraceInTheCounters()
    {
        // The failure this issue is about is queue depth that only ever grows. A rejection that leaked a
        // count would reintroduce it one rejection at a time.
        var limiter = CreateLimiter(maxQueueLength: 0);
        var (running, release) = await Saturate(limiter);
        var before = limiter.GetCurrentState();

        for (var i = 0; i < 5; i++)
        {
            var act = () => Execute(limiter, _ => Task.FromResult(true));
            await act.Should().ThrowAsync<ExecuteLimiterQueueFullException>();
        }

        var after = limiter.GetCurrentState();
        after.QueueCount.Should().Be(before.QueueCount);
        limiter.GetInFlightCalls().Should().HaveCount(1, "only the blocker is still in flight");

        release.SetResult();
        await running.WaitAsync(Timeout);
    }

    [Fact]
    public async Task ThePoolKeepsWorking_AfterRejections()
    {
        var limiter = CreateLimiter(maxQueueLength: 0);
        var (running, release) = await Saturate(limiter);
        var act = () => Execute(limiter, _ => Task.FromResult(true));
        await act.Should().ThrowAsync<ExecuteLimiterQueueFullException>();

        release.SetResult();
        await running.WaitAsync(Timeout);
        var (result, _) = await Execute(limiter, _ => Task.FromResult(true));

        result.Should().BeTrue();
    }

    // --- Timeout ---

    [Fact]
    public async Task AWaiterBeyondTheTimeout_IsRejected()
    {
        var limiter = CreateLimiter(queueTimeout: TimeSpan.FromMilliseconds(100));
        var (running, release) = await Saturate(limiter);

        var act = () => Execute(limiter, _ => Task.FromResult(true));

        var exception = await act.Should().ThrowAsync<ExecuteLimiterQueueTimeoutException>();
        exception.Which.QueueTimeout.Should().Be(TimeSpan.FromMilliseconds(100));
        exception.Which.ServerKey.Should().Be(ServerKey);

        release.SetResult();
        await running.WaitAsync(Timeout);
    }

    [Fact]
    public async Task ATimedOutWaiter_DoesNotLeakItsSlot()
    {
        var limiter = CreateLimiter(queueTimeout: TimeSpan.FromMilliseconds(100));
        var (running, release) = await Saturate(limiter);
        var act = () => Execute(limiter, _ => Task.FromResult(true));
        await act.Should().ThrowAsync<ExecuteLimiterQueueTimeoutException>();
        release.SetResult();
        await running.WaitAsync(Timeout);

        var (result, _) = await Execute(limiter, _ => Task.FromResult(true));

        result.Should().BeTrue();
        limiter.GetCurrentState().QueueCount.Should().Be(0);
    }

    [Fact]
    public async Task AWaiterWithinTheTimeout_Succeeds()
    {
        var limiter = CreateLimiter(queueTimeout: TimeSpan.FromSeconds(30));
        var (running, release) = await Saturate(limiter);
        var waiter = Execute(limiter, _ => Task.FromResult(true));
        await WaitUntil(() => PoolState(limiter).QueueCount >= 1);

        release.SetResult();
        await running.WaitAsync(Timeout);

        (await waiter.WaitAsync(Timeout)).Result.Should().BeTrue();
    }

    // --- Defaults preserve today's behaviour ---

    [Fact]
    public async Task WithNoLimits_WaitersQueueRatherThanBeingRejected()
    {
        // The lifted-comparison trap: `waitingCount <= (int?)null` is false, so a null limit must be checked
        // explicitly or the default configuration rejects every waiter.
        var limiter = CreateLimiter();
        var (running, release) = await Saturate(limiter);

        var waiters = Enumerable.Range(0, 20).Select(_ => Execute(limiter, _ => Task.FromResult(true))).ToArray();
        await WaitUntil(() => PoolState(limiter).QueueCount >= 20);
        release.SetResult();
        await running.WaitAsync(Timeout);
        await Task.WhenAll(waiters).WaitAsync(Timeout);

        waiters.Should().OnlyContain(x => x.Result.Result);
        PoolState(limiter).RejectedCount.Should().Be(0);
    }

    // --- Observability ---

    [Fact]
    public async Task RejectionsAreCounted_PerPool()
    {
        var limiter = CreateLimiter(maxQueueLength: 0);
        var (running, release) = await Saturate(limiter);

        for (var i = 0; i < 3; i++)
        {
            var act = () => Execute(limiter, _ => Task.FromResult(true));
            await act.Should().ThrowAsync<ExecuteLimiterQueueFullException>();
        }

        PoolState(limiter).RejectedCount.Should().Be(3);

        release.SetResult();
        await running.WaitAsync(Timeout);
    }

    [Fact]
    public async Task TheWarningIsEdgeTriggered_NotOncePerRejection()
    {
        // Logging one line per rejected operation would reproduce the original incident in the logging
        // pipeline — the reported burst was ~11,900 operations.
        var logger = new CountingLogger();
        var limiter = CreateLimiter(maxQueueLength: 0, logger: logger);
        var (running, release) = await Saturate(limiter);

        for (var i = 0; i < 50; i++)
        {
            var act = () => Execute(limiter, _ => Task.FromResult(true));
            await act.Should().ThrowAsync<ExecuteLimiterQueueFullException>();
        }

        logger.Warnings.Should().Be(1);
        PoolState(limiter).RejectedCount.Should().Be(50);

        release.SetResult();
        await running.WaitAsync(Timeout);
    }

    [Fact]
    public async Task TheWarningReArms_OnceTheQueueDrainsThroughWork()
    {
        var logger = new CountingLogger();
        var limiter = CreateLimiter(maxQueueLength: 1, logger: logger);

        for (var episode = 0; episode < 2; episode++)
        {
            var (running, release) = await Saturate(limiter);
            var waiter = Execute(limiter, _ => Task.FromResult(true));
            await WaitUntil(() => PoolState(limiter).QueueCount >= 1);

            var act = () => Execute(limiter, _ => Task.FromResult(true));
            await act.Should().ThrowAsync<ExecuteLimiterQueueFullException>();

            release.SetResult();
            await running.WaitAsync(Timeout);
            await waiter.WaitAsync(Timeout);
        }

        logger.Warnings.Should().Be(2);
    }

    // --- Configuration validation ---

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    public void ANegativeQueueLength_IsRejectedAtConstruction(int maxQueueLength)
    {
        var act = () => CreateLimiter(maxQueueLength: maxQueueLength);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ANonPositiveTimeout_IsRejectedAtConstruction()
    {
        var act = () => CreateLimiter(queueTimeout: TimeSpan.Zero);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void TheDefaults_AreUnbounded()
    {
        var options = new ExecuteLimiterOptions();

        options.MaxQueueLength.Should().BeNull();
        options.QueueTimeout.Should().BeNull();
    }

    /// <summary>
    /// Counts only the queue-full warning. The limiter also warns when a pool reaches its concurrency limit,
    /// which at these pool sizes happens on every execution, so counting all warnings measures the wrong thing.
    /// </summary>
    private sealed class CountingLogger : ILogger<ExecuteLimiter>
    {
        private const string QueueFullMarker = "execute queue for";
        private int _warnings;

        public int Warnings => Volatile.Read(ref _warnings);

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (logLevel != LogLevel.Warning) return;
            if (formatter(state, exception).Contains(QueueFullMarker, StringComparison.OrdinalIgnoreCase))
                Interlocked.Increment(ref _warnings);
        }
    }
}
