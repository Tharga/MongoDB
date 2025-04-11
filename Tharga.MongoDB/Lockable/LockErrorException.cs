namespace Tharga.MongoDB.Lockable;

public class LockErrorException : LockException
{
    public LockErrorException(string message)
        : base(message)
    {
    }
}