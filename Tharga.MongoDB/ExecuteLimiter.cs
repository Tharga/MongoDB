using System;
using System.Threading;
using System.Threading.Tasks;

namespace Tharga.MongoDB;

internal class ExecuteLimiter : IExecuteLimiter
{
    private int _ongoingCount;
    private readonly SemaphoreSlim _semaphoreSlim = new(20, 20);

    public int OngoingCount => _ongoingCount;

    public async Task<T> ExecuteAsync<T>(string key, Func<Task<T>> action)
    {
        await _semaphoreSlim.WaitAsync();
        try
        {
            _ongoingCount++;
            return await action.Invoke();
        }
        finally
        {
            _ongoingCount--;
            _semaphoreSlim.Release();
        }
    }
}