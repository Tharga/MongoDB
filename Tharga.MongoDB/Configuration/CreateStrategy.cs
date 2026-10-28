using System;

namespace Tharga.MongoDB.Configuration;

public enum CreateStrategy
{
    /// <summary>
    /// Behaves as <see cref="CreateOnAdd"/>. Empty collections used to be dropped automatically, which lost documents
    /// added between the emptiness check and the drop. Call <see cref="IRepositoryCollection.DropIfEmptyAsync"/> to
    /// drop an empty collection.
    /// </summary>
    [Obsolete("Empty collections are no longer dropped automatically, and this value behaves as CreateOnAdd. Call IRepositoryCollection.DropIfEmptyAsync to drop an empty collection.")]
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