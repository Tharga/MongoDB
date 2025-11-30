using System;

namespace Tharga.MongoDB.Configuration;

public record MongoDbConfig
{
    public MongoDbApiAccess AccessInfo { get; init; }
    public int? ResultLimit { get; init; }
    public bool AutoClean { get; init; }
    public bool CleanOnStartup { get; init; }

    [Obsolete($"Use {nameof(CreateCollectionStrategy)} instead")]
    public bool DropEmptyCollections { get; init; }

    public CreateStrategy CreateCollectionStrategy { get; init; }
}