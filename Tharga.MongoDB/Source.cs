using System;
using System.Threading.Tasks;

namespace Tharga.MongoDB;

[Flags]
public enum Source
{
    Database = 1,
    Registration = 2,
    Monitor = 4
}

internal interface IExecuteLimiter
{
    Task<T> ExecuteAsync<T>(string key, Func<Task<T>> action);
}

internal class ExecuteLimiter : IExecuteLimiter
{
    private int _ongoingCount;

    public int OngoingCount => _ongoingCount;

    public Task<T> ExecuteAsync<T>(string key, Func<Task<T>> action)
    {
        try
        {
            _ongoingCount++;
            if (_ongoingCount > 1)
            {
            }
            else
            {
            }

            return action.Invoke();
        }
        finally
        {
            _ongoingCount--;
        }
    }
}