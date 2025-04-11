namespace Tharga.MongoDB.Lockable;

public class UnknownException : PickException
{
    public UnknownException(string message)
        : base(message)
    {
    }
}