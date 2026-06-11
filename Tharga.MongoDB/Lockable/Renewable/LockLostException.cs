namespace Tharga.MongoDB.Lockable.Renewable;

/// <summary>
/// Thrown when a renewal (extend / keep-alive) finds the lock is no longer owned by this lease —
/// the document no longer exists, or its <c>LockKey</c> no longer matches because another writer
/// has since acquired the lock (the expiry recovery path picked it up). Distinct from
/// <see cref="LockExpiredException"/>, which signals a strict-TTL collection refusing an expired
/// renewal even though the <c>LockKey</c> still matches.
/// </summary>
public class LockLostException : LockException
{
    public LockLostException(string message)
        : base(message)
    {
    }
}
