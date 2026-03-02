namespace Tharga.MongoDB.Lockable;

public enum DeleteMode
{
    /// <summary>
    /// Unlocked or expired.
    /// </summary>
    Unlocked,
    ExceptionOnly,
    LockedOnly,
    Any
}