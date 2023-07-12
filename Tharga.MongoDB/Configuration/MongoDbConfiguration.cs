namespace Tharga.MongoDB.Configuration;

public record MongoDbConfiguration
{
    /// <summary>
    /// If provided the Atlas MongoDB firewall will be opened for the machine that makes the call.
    /// Default value is null.
    /// </summary>
    public MongoDbApiAccess AccessInfo { get; init; }

    /// <summary>
    /// The maximum number of records allowed in a single response.
    /// The function GetPageAsync can be used to make multiple calls to get more data.
    /// Default value is null (no limit).
    /// </summary>
    public int? ResultLimit { get; init; }

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
    /// Drops the collection when the last item is deleted. It also drops empty collection on access, if CleanOnStartup is set to true.
    /// Default value is true.
    /// </summary>
    public bool? DropEmptyCollections { get; init; }
}