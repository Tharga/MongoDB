using System;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

namespace Tharga.MongoDB.Lockable;

public static class EntityScopeExtensions
{
    /// <summary>
    /// The provided function should return an entity for commit and default for abandon.
    /// If this is a delete operation, return the entity to be deleted from 'func'.
    /// </summary>
    /// <typeparam name="T">Type of the entity.</typeparam>
    /// <typeparam name="TKey">Type of the key.</typeparam>
    /// <param name="item">The entity scope.</param>
    /// <param name="func">The function to be executed within the scope.</param>
    /// <param name="errorHandler">
    /// Receives a <see cref="LockableErrorKind"/> discriminator and the exception for every failure path —
    /// errors thrown by <paramref name="func"/> as well as commit-side exceptions
    /// (<see cref="LockExpiredException"/>, <see cref="LockAlreadyReleasedException"/>, <see cref="CommitException"/>,
    /// any other commit failure). When <c>null</c> (default), the original exception propagates with its
    /// stack trace intact — same behavior as before this overload existed.
    /// </param>
    /// <exception cref="ArgumentNullException"></exception>
    public static async Task<T> ExecuteAsync<T, TKey>(this EntityScope<T, TKey> item, Func<T, Task<T>> func, Action<LockableErrorKind, Exception> errorHandler = null)
        where T : LockableEntityBase<TKey>
    {
        if (item == null) return null;
        if (func == null) throw new ArgumentNullException(nameof(func), $"{nameof(func)} needs to be provided.");

        T entity = default;

        try
        {
            entity = await func.Invoke(item.Entity);
            if (entity == null)
            {
                await item.AbandonAsync();
                return null;
            }
        }
        catch (Exception e)
        {
            await item.SetErrorStateAsync(e);
            if (errorHandler != null)
            {
                errorHandler.Invoke(LockableErrorKind.HandlerError, e);
                return entity ?? item.Entity;
            }

            throw;
        }

        try
        {
            return await item.CommitAsync(entity);
        }
        catch (Exception e) when (errorHandler != null)
        {
            var kind = e switch
            {
                LockExpiredException => LockableErrorKind.LockExpired,
                LockAlreadyReleasedException => LockableErrorKind.LockAlreadyReleased,
                _ => LockableErrorKind.CommitError,
            };
            errorHandler.Invoke(kind, e);
            return entity;
        }
    }

    /// <summary>
    /// Backwards-compatible overload — pass an <see cref="Action{Exception}"/> to handle
    /// errors thrown by <paramref name="func"/> only; commit-side exceptions still propagate.
    /// New code should prefer the <see cref="LockableErrorKind"/> overload, which routes every
    /// failure path through a single callback.
    /// </summary>
    public static Task<T> ExecuteAsync<T, TKey>(this EntityScope<T, TKey> item, Func<T, Task<T>> func, Action<Exception> errorHandler)
        where T : LockableEntityBase<TKey>
    {
        if (errorHandler == null) throw new ArgumentNullException(nameof(errorHandler), $"{nameof(errorHandler)} needs to be provided. Pass null to the {nameof(LockableErrorKind)} overload to skip error handling.");

        return ExecuteAsync(item, func, (kind, e) =>
        {
            if (kind == LockableErrorKind.HandlerError)
            {
                errorHandler.Invoke(e);
            }
            else
            {
                ExceptionDispatchInfo.Capture(e).Throw();
            }
        });
    }
}
