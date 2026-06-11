using System;

namespace Tharga.MongoDB.Lockable;

/// <summary>
/// Result of a call to <c>ExtendLockAsync</c> on an <see cref="EntityScope{T, TKey}"/> or
/// <see cref="LockScope{T, TKey}"/>.
/// </summary>
public record LockExtensionResult
{
    /// <summary>
    /// The lock's expiry after this call — the freshly written expiry when <see cref="Extended"/> is
    /// <c>true</c>, otherwise the existing (still-valid) expiry from the most recent write.
    /// </summary>
    public required DateTime ExpireTime { get; init; }

    /// <summary>
    /// <c>true</c> when this call wrote a new expiry to the database; <c>false</c> when it was throttled
    /// (less than the collection's minimum extend interval since the last write) and therefore a no-op.
    /// </summary>
    public required bool Extended { get; init; }
}
