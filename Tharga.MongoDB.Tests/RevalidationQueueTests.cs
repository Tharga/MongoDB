using FluentAssertions;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Tharga.MongoDB.Tests;

public class RevalidationQueueTests
{
    [Fact]
    public async Task HighPriorityKeys_DrainBeforeLow()
    {
        var order = new ConcurrentQueue<string>();
        var done = new SemaphoreSlim(0);

        // Concurrency cap of 1 so refreshes are serialised, and a deferred start so the loop cannot
        // drain anything until all four keys are pending. With an eager start this test raced: every
        // Enqueue signals the loop, so under thread-pool load both lows could be dequeued before the
        // highs were enqueued. That is correct queue behaviour — priority only decides between items
        // pending at the same time — but it made the ordering assertion non-deterministic.
        using var sut = new RevalidationQueue((key, _) =>
        {
            order.Enqueue(key);
            done.Release();
            return Task.CompletedTask;
        }, maxConcurrent: 1, startImmediately: false);

        // Pre-load the queue with both priorities BEFORE the loop has drained anything.
        sut.EnqueueLow("low-1");
        sut.EnqueueLow("low-2");
        sut.EnqueueHigh("high-1");
        sut.EnqueueHigh("high-2");

        sut.Start();

        for (var i = 0; i < 4; i++)
        {
            (await done.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();
        }

        var observed = order.ToArray();
        observed.Should().HaveCount(4);
        // Both high-priority keys must drain before either low-priority one.
        observed.Should().Equal("high-1", "high-2", "low-1", "low-2");
    }

    [Fact]
    public async Task DuplicateEnqueue_IsCoalesced()
    {
        var count = 0;
        var done = new SemaphoreSlim(0);

        // Deferred start, for the same reason as the test above. Coalescing means "dedupe while pending":
        // the drain loop drops the key from the enqueued set as soon as it dequeues it, before the refresh
        // runs, so a key enqueued again after that point is legitimately a second refresh. With an eager
        // start this test raced the loop -- under thread-pool load it could dequeue before all 50 enqueues
        // had run, and observe 2. Pre-loading the queue before Start makes the coalescing deterministic.
        using var sut = new RevalidationQueue((_, _) =>
        {
            Interlocked.Increment(ref count);
            done.Release();
            return Task.CompletedTask;
        }, maxConcurrent: 1, startImmediately: false);

        for (var i = 0; i < 50; i++) sut.EnqueueLow("same-key");

        sut.Start();

        (await done.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue("the coalesced key should refresh once");
        (await done.WaitAsync(TimeSpan.FromMilliseconds(200))).Should().BeFalse("no second refresh should follow");

        count.Should().Be(1, "duplicate keys in the queue should coalesce into one refresh");
    }

    [Fact]
    public async Task ConcurrencyCap_NeverExceeded()
    {
        var concurrent = 0;
        var maxObserved = 0;
        var done = new SemaphoreSlim(0);

        using var sut = new RevalidationQueue(async (_, _) =>
        {
            var current = Interlocked.Increment(ref concurrent);
            InterlockedMax(ref maxObserved, current);
            await Task.Delay(50);
            Interlocked.Decrement(ref concurrent);
            done.Release();
        }, maxConcurrent: 4);

        for (var i = 0; i < 20; i++) sut.EnqueueLow($"k{i}");

        for (var i = 0; i < 20; i++)
        {
            (await done.WaitAsync(TimeSpan.FromSeconds(10))).Should().BeTrue();
        }

        maxObserved.Should().BeLessThanOrEqualTo(4);
    }

    [Fact]
    public async Task Dispose_StopsProcessing()
    {
        var processed = 0;
        var sut = new RevalidationQueue((_, _) =>
        {
            Interlocked.Increment(ref processed);
            return Task.Delay(50);
        }, maxConcurrent: 2);

        for (var i = 0; i < 10; i++) sut.EnqueueLow($"k{i}");
        await Task.Delay(60); // let a few items start
        sut.Dispose();

        var snapshot = processed;
        await Task.Delay(300); // give it time to keep processing if it would
        processed.Should().Be(snapshot, "no new refresh callbacks should fire after Dispose");
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int initial;
        do { initial = target; if (value <= initial) return; }
        while (Interlocked.CompareExchange(ref target, value, initial) != initial);
    }
}
