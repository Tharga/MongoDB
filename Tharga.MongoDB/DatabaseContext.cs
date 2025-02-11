using Microsoft.Extensions.Logging;
using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB;

public record DatabaseContext
{
    /// <summary>
    /// If specified the collection will have this name.
    /// </summary>
    public string CollectionName { get; init; }

    /// <summary>
    /// Part of the database name that can be appended by the IConnectionStringBuilder. Normally the {part} is replaced by this value.
    /// </summary>
    public string DatabasePart { get; init; }

    /// <summary>
    /// Configuration name for the database. Not to be confuced with the name that is actually used to stor data in the database.
    /// This name is used to read data from DatabaseOptions or IConfiguration.
    /// </summary>
    public ConfigurationName ConfigurationName { get; init; }
}