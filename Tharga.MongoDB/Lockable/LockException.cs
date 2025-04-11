namespace Tharga.MongoDB.Lockable;

public class LockException : PickException
{
    public LockException(string message)
        : base(message)
    {
    }
}