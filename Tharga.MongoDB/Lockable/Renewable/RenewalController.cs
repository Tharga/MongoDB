using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Tharga.MongoDB.Lockable.Renewable;

/// <summary>
/// Owns the renewal lifecycle for a single lease: the current lock reference (<see cref="LockState"/>),
/// the extend delegate, the keep-alive background loop, and the <see cref="LockLost"/> cancellation
/// signal raised when a renewal discovers the lock has been lost. All extension attempts are
/// serialized through a single gate so the keep-alive loop and an explicit <see cref="ExtendAsync"/>
/// never race on the lock reference.
/// </summary>
internal sealed class RenewalController
{
    private readonly LockState _lockState;
    private readonly Func<Lock, TimeSpan?, Task<Lock>> _extend;
    private readonly TimeSpan _originalTimeout;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lockLostCts = new();

    private CancellationTokenSource _loopCts;
    private Task _loopTask;
    private bool _keepAliveStarted;
    private bool _stopped;

    public RenewalController(LockState lockState, Func<Lock, TimeSpan?, Task<Lock>> extend, TimeSpan originalTimeout, ILogger logger)
    {
        _lockState = lockState;
        _extend = extend;
        _originalTimeout = originalTimeout;
        _logger = logger;
    }

    public CancellationToken LockLost => _lockLostCts.Token;

    public async Task<DateTime> ExtendAsync(TimeSpan? extension = null)
    {
        await _gate.WaitAsync();
        try
        {
            if (_stopped) throw new LockAlreadyReleasedException("Cannot extend a lock that has already been released.");

            var current = _lockState.Current;
            try
            {
                var renewed = await _extend.Invoke(current, extension);
                _lockState.Current = renewed;
                return renewed.ExpireTime;
            }
            catch (LockLostException)
            {
                if (!_lockLostCts.IsCancellationRequested) _lockLostCts.Cancel();
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public IAsyncDisposable StartKeepAlive(LockKeepAliveOptions options = null)
    {
        if (_keepAliveStarted) throw new InvalidOperationException("Keep-alive has already been started for this lease.");
        if (_stopped) throw new InvalidOperationException("Cannot start keep-alive on a lease that has already been released.");

        _keepAliveStarted = true;
        _loopCts = new CancellationTokenSource();
        _loopTask = Task.Run(() => KeepAliveLoopAsync(options, _loopCts.Token));

        return new KeepAliveHandle(this);
    }

    private async Task KeepAliveLoopAsync(LockKeepAliveOptions options, CancellationToken loopCt)
    {
        var interval = options?.Interval ?? TimeSpan.FromTicks(_originalTimeout.Ticks / 3);
        if (interval <= TimeSpan.Zero) interval = TimeSpan.FromMilliseconds(1);
        var extensionSpan = options?.Extension ?? _originalTimeout;
        var maxTotalDuration = options?.MaxTotalDuration ?? TimeSpan.FromHours(4);
        var onRenewalFailure = options?.OnRenewalFailure;

        var elapsed = Stopwatch.StartNew();

        while (!loopCt.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, loopCt);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (elapsed.Elapsed >= maxTotalDuration)
            {
                _logger?.LogWarning("Keep-alive for lease stopped: anti-zombie cap of {MaxTotalDuration} reached without release.", maxTotalDuration);
                return;
            }

            try
            {
                await ExtendAsync(extensionSpan);
            }
            catch (LockLostException e)
            {
                onRenewalFailure?.Invoke(e);
                return;
            }
            catch (LockExpiredException e)
            {
                onRenewalFailure?.Invoke(e);
                return;
            }
            catch (LockAlreadyReleasedException)
            {
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e)
            {
                onRenewalFailure?.Invoke(e);
                _logger?.LogWarning(e, "Keep-alive renewal failed transiently; will retry next tick. {Message}", e.Message);
            }
        }
    }

    public async Task StopAsync()
    {
        if (_stopped) return;
        _stopped = true;

        _loopCts?.Cancel();

        if (_loopTask != null)
        {
            try
            {
                await _loopTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (TimeoutException)
            {
                _logger?.LogWarning("Keep-alive loop did not stop within the timeout during release.");
            }
            catch (Exception e)
            {
                _logger?.LogWarning(e, "Keep-alive loop faulted while stopping during release. {Message}", e.Message);
            }
        }
    }

    private sealed class KeepAliveHandle : IAsyncDisposable
    {
        private readonly RenewalController _controller;
        private bool _disposed;

        public KeepAliveHandle(RenewalController controller)
        {
            _controller = controller;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            await _controller.StopAsync();
        }
    }
}
