using System;

namespace Tharga.MongoDB;

public class CollectionDroppedEventArgs : EventArgs
{
    public CollectionDroppedEventArgs(DatabaseContext databaseContext)
    {
        DatabaseContext = databaseContext;
    }

    public CollectionDroppedEventArgs(DatabaseContext databaseContext, string configurationName, string databaseName, string collectionName)
        : this(databaseContext)
    {
        ConfigurationName = configurationName;
        DatabaseName = databaseName;
        CollectionName = collectionName;
    }

    public DatabaseContext DatabaseContext { get; }

    /// <summary>
    /// Resolved configuration name of the dropped collection, when known. Unlike
    /// <see cref="DatabaseContext"/> (which carries only the registration-time context and can be
    /// null or lack the effective collection name), these resolved fields identify the physical
    /// collection the same way it was reported — letting the monitor match and forward it precisely.
    /// </summary>
    public string ConfigurationName { get; }

    /// <summary>Resolved physical database name of the dropped collection, when known.</summary>
    public string DatabaseName { get; }

    /// <summary>Resolved physical (protected) collection name of the dropped collection, when known.</summary>
    public string CollectionName { get; }
}
