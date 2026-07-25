using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Tharga.MongoDB;

/// <summary>
/// Priority-aware background revalidator with a small concurrency cap.
/// High-priority keys drain before low-priority ones. Per-key refresh
/// callback fires on a background task. Used by the Blazor admin's
/// CollectionView to keep visible rows fresh first while a background
/// sweep refreshes off-screen rows without hammering Mongo with thousands
/// of simultaneous fetches.
/// </summary>
public sealed class RevalidationQueue : IDisposable
{
    private readonly Func<string, CancellationToken, Task> _refresh;
    private readonly SemaphoreSlim _gate;
    private readonly ConcurrentQueue<string> _high = new();
    private readonly ConcurrentQueue<string> _low = new();
    private readonly HashSet<string> _enqueued = new();
    private readonly object _enqLock = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _signal = new(0, int.MaxValue);
    private Task _loop;
    private int _disposed;
    private int _started;

    public RevalidationQueue(Func<string, CancellationToken, Task> refresh, int maxConcurrent = 16)
        : this(refresh, maxConcurrent, startImmediately: true)
    {
    }

    /// <summary>
    /// Deferred-start overload. With <paramref name="startImmediately"/> false the drain loop does
    /// not run until <see cref="Start"/> is called, so a caller can fully populate both priority
    /// queues first. Exists so ordering can be asserted deterministically — with an eager start the
    /// loop may drain the first-enqueued items before later, higher-priority ones arrive, which is
    /// correct behaviour but makes "high before low" untestable.
    /// </summary>
    internal RevalidationQueue(Func<string, CancellationToken, Task> refresh, int maxConcurrent, bool startImmediately)
    {
        _refresh = refresh ?? throw new ArgumentNullException(nameof(refresh));
        _gate = new SemaphoreSlim(maxConcurrent, maxConcurrent);
        if (startImmediately) Start();
    }

    /// <summary>Starts the drain loop. Idempotent.</summary>
    internal void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) return;
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    public void EnqueueHigh(string key) => Enqueue(key, high: true);

    public void EnqueueLow(string key) => Enqueue(key, high: false);

    public void EnqueueHighMany(IEnumerable<string> keys)
    {
        foreach (var key in keys) EnqueueHigh(key);
    }

    public void EnqueueLowMany(IEnumerable<string> keys)
    {
        foreach (var key in keys) EnqueueLow(key);
    }

    private void Enqueue(string key, bool high)
    {
        if (string.IsNullOrEmpty(key)) return;

        lock (_enqLock)
        {
            if (!_enqueued.Add(key)) return;
        }

        (high ? _high : _low).Enqueue(key);
        _signal.Release();
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _signal.WaitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }

            if (!_high.TryDequeue(out var key) && !_low.TryDequeue(out key)) continue;

            lock (_enqLock) { _enqueued.Remove(key); }

            try
            {
                await _gate.WaitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }

            _ = Task.Run(async () =>
            {
                try
                {
                    await _refresh(key, ct).ConfigureAwait(false);
                }
                catch
                {
                    // Per-key failures are best-effort; swallow to keep the queue alive.
                }
                finally
                {
                    _gate.Release();
                }
            }, ct);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { _cts.Cancel(); } catch { }
        try { _signal.Release(int.MaxValue / 2); } catch { } // wake the loop
        // Don't await _loop — let it drain naturally on the background thread.
        _cts.Dispose();
        _gate.Dispose();
        _signal.Dispose();
    }
}
