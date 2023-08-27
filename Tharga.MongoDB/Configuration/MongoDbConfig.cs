namespace Tharga.MongoDB.Configuration;

public record MongoDbConfig
{
    public MongoDbApiAccess AccessInfo { get; init; }
    public int? ResultLimit { get; init; }
    public bool AutoClean { get; init; }
    public bool CleanOnStartup { get; init; }
    public bool DropEmptyCollections { get; init; }
}