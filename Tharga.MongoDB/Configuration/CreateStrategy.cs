namespace Tharga.MongoDB.Configuration;

public enum CreateStrategy
{
    /// <summary>
    /// Create collection if it does not exist when a record is added and drop when the collection is empty.
    /// A collection that declares a unique index is never dropped, since other processes would keep writing to it
    /// without that index once it is recreated.
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