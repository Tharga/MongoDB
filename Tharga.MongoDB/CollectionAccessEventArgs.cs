using System;

namespace Tharga.MongoDB;

public class CollectionAccessEventArgs : EventArgs
{
    public CollectionAccessEventArgs(DatabaseContext databaseContext, string server, Type entityType, Type collectionType)
    {
        DatabaseContext = databaseContext;
        Server = server;
        EntityType = entityType;
        CollectionType = collectionType;
    }

    public DatabaseContext DatabaseContext { get; }
    public string Server { get; }
    public Type EntityType { get; }
    public Type CollectionType { get; }
}