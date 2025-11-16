using System;

namespace Tharga.MongoDB;

public class CollectionAccessEventArgs : EventArgs
{
    public CollectionAccessEventArgs(DatabaseContext databaseContext, string server, Type entityType)
    {
        DatabaseContext = databaseContext;
        Server = server;
        EntityType = entityType;
    }

    public DatabaseContext DatabaseContext { get; }
    public string Server { get; }
    public Type EntityType { get; }
}