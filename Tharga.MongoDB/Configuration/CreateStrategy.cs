namespace Tharga.MongoDB.Configuration;

public enum CreateStrategy
{
    /// <summary>
    /// Create collection if it does not exist when a record is added and drop when the collection is empty.
    /// </summary>
    DropEmpty,

    /// <summary>
    /// Create collection if it does not exist when a record is added. Do not automatically drop empty collections.
    /// </summary>
    CreateOnAdd,

    /// <summary>
    /// Create collection if it does not exist on get and add calls. Do not automatically drop empty collections.
    /// </summary>
    CreateOnGet,
}