using System;
using System.Threading;
using System.Threading.Tasks;

namespace Tharga.MongoDB;

internal interface IExecuteLimiter
{
    Task<(T Result, ExecuteInfo Info)> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, string key = default, CancellationToken cancellationToken = default);
}