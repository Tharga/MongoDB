namespace Tharga.MongoDB.Lockable.Renewable;

/// <summary>
/// Mutable holder of the current <see cref="Lock"/> reference for a single lease. <see cref="Lock"/>
/// is immutable; a successful renewal swaps the reference here so that the release path reads the
/// renewed <c>ExpireTime</c>/<c>LockKey</c> rather than the original lock.
/// </summary>
internal sealed class LockState
{
    private volatile Lock _current;

    public LockState(Lock current)
    {
        _current = current;
    }

    public Lock Current
    {
        get => _current;
        set => _current = value;
    }
}
