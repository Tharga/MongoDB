namespace Tharga.MongoDB.Configuration;

public record MongoDbConfig
{
    public MongoDbApiAccess AccessInfo { get; init; }
    public int? FetchSize { get; init; }
    public bool AutoClean { get; init; }
    public bool CleanOnStartup { get; init; }
    public CreateStrategy CreateCollectionStrategy { get; init; }
}