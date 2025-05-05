using System;
using System.Threading.Tasks;

namespace Tharga.MongoDB.Lockable;

public static class EntityScopeExtensions
{
    /// <summary>
    /// The provided function should return an antity for commit and default for abandon.
    /// If this is a delete operation, return the entity to be deleted from 'func'.
    /// </summary>
    /// <typeparam name="T">Type of the entity.</typeparam>
    /// <typeparam name="TKey">Type of the key.</typeparam>
    /// <param name="item">The entity scope.</param>
    /// <param name="func">The function to be executed within the scope.</param>
    /// <returns></returns>
    /// <exception cref="ArgumentNullException"></exception>
    public static async Task<T> ExecuteAsync<T, TKey>(this EntityScope<T, TKey> item, Func<T, Task<T>> func)
        where T : LockableEntityBase<TKey>
    {
        if (item == default) return default;
        if (func == null) throw new ArgumentNullException(nameof(func), $"{nameof(func)} needs to be provided.");

        T entity;
        try
        {
            entity = await func.Invoke(item.Entity);
            if (entity == default)
            {
                await item.AbandonAsync();
                return default;
            }
        }
        catch (Exception e)
        {
            await item.SetErrorStateAsync(e);
            throw;
        }

        return await item.CommitAsync(entity);
    }
}