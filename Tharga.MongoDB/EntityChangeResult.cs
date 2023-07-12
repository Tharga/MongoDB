using System;
using System.Threading.Tasks;

namespace Tharga.MongoDB;

public record EntityChangeResult<TEntity>
{
    private readonly Func<ValueTask<TEntity>> _afterLoader;

    public readonly TEntity Before;
    private TEntity _after;
    private bool _afterLoaded;

    internal EntityChangeResult(TEntity before, TEntity after)
    {
        Before = before;
        _after = after;
        _afterLoaded = true;
    }

    internal EntityChangeResult(TEntity before, Func<ValueTask<TEntity>> afterLoader)
    {
        Before = before;
        _afterLoader = afterLoader;
    }

    public async ValueTask<TEntity> GetAfterAsync()
    {
        if (!_afterLoaded)
        {
            _after = await _afterLoader();
            _afterLoaded = true;
        }

        return _after;
    }
}