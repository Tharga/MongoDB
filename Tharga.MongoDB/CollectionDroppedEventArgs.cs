using System;

namespace Tharga.MongoDB;

public class CollectionDroppedEventArgs : EventArgs
{
    public CollectionDroppedEventArgs(DatabaseContext databaseContext)
    {
        DatabaseContext = databaseContext;
    }

    public DatabaseContext DatabaseContext { get; }
}