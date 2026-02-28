using System;

namespace Tharga.MongoDB.Configuration;

public record MongoDbConfiguration
{
    /// <summary>
    /// If provided the Atlas MongoDB firewall will be opened for the machine that makes the call.
    /// Default value is null.
    /// </summary>
    public MongoDbApiAccess AccessInfo { get; init; }

    /// <summary>
    /// The number of documents fetched from the database per call when streaming with GetAsync.
    /// When Options.Limit is not set, this acts as the batch size for paging through all results.
    /// Default value is null (falls back to 1000).
    /// </summary>
    public int? FetchSize { get; init; }

    /// <summary>
    /// Automatically clean database records and store back to the database when they are accessed.
    /// This feature is triggered by Values in the entity´s 'CatchAll', that can be cleaned in the 'EndInit' method.
    /// Default value is true.
    /// </summary>
    public bool? AutoClean { get; init; }

    /// <summary>
    /// Finds all items that needs cleaning when the first record is accessed. This method is only executed once in the lifetime of the application.
    /// Default value is false.
    /// </summary>
    public bool? CleanOnStartup { get; init; }

    /// <summary>
    /// Default value is DropEmpty, meaning collections are created when the first entity is added and removed when the last entity is deleted. Change this behaviour with this configuration.
    /// </summary>
    public CreateStrategy CreateCollectionStrategy { get; init; }
}