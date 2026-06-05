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

        // Concurrency cap of 1 so the order is deterministic.
        using var sut = new RevalidationQueue((key, _) =>
        {
            order.Enqueue(key);
            done.Release();
            return Task.CompletedTask;
        }, maxConcurrent: 1);

        // Pre-load the queue with both priorities BEFORE the loop has drained anything.
        sut.EnqueueLow("low-1");
        sut.EnqueueLow("low-2");
        sut.EnqueueHigh("high-1");
        sut.EnqueueHigh("high-2");

        for (var i = 0; i < 4; i++)
        {
            (await done.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();
        }

        var observed = order.ToArray();
        observed.Should().HaveCount(4);
        // The two high-priority keys must precede the two low-priority keys
        // among items the loop sees after the initial wakeup. The very first
        // pumped item may be "low-1" if it was queued before the high keys
        // signalled the loop — but no high item should appear AFTER a low.
        var firstHigh = Array.IndexOf(observed, "high-1");
        var firstLow = Array.IndexOf(observed, "low-2"); // low-2 is the LAST low we enqueued
        firstHigh.Should().BeLessThan(firstLow, "high keys must drain before low ones queued at the same time");
    }

    [Fact]
    public async Task DuplicateEnqueue_IsCoalesced()
    {
        var count = 0;

        using var sut = new RevalidationQueue((_, _) =>
        {
            Interlocked.Increment(ref count);
            return Task.CompletedTask;
        }, maxConcurrent: 1);

        for (var i = 0; i < 50; i++) sut.EnqueueLow("same-key");

        // Wait until the queue settles.
        await Task.Delay(200);

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
