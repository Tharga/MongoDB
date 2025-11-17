using System;

namespace Tharga.MongoDB;

public class CollectionAccessEventArgs : EventArgs
{
    public CollectionAccessEventArgs(DatabaseContext databaseContext, string server, Type entityType, string collectionName)
    {
        DatabaseContext = databaseContext;
        Server = server;
        EntityType = entityType;
        CollectionName = collectionName;
    }

    public DatabaseContext DatabaseContext { get; }
    public string Server { get; }
    public Type EntityType { get; }
    public string CollectionName { get; }
}