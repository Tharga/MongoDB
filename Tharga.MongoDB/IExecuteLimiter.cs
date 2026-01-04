using System;
using System.Threading.Tasks;

namespace Tharga.MongoDB;

internal interface IExecuteLimiter
{
    Task<T> ExecuteAsync<T>(string key, Func<Task<T>> action);
}