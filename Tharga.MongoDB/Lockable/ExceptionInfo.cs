namespace Tharga.MongoDB.Lockable;

public record ExceptionInfo
{
    public string Type { get; init; }
    public string Message { get; init; }
    public string StackTrace { get; init; }

    //[BsonIgnoreIfDefault]
    //public Dictionary<string, object> Data { get; init; }
    //
    //[BsonIgnoreIfDefault]
    //public ExceptionInfo Inner { get; init; }
}