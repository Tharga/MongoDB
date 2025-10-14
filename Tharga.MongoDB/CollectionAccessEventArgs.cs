using System;

namespace Tharga.MongoDB;

public class CollectionAccessEventArgs : EventArgs
{
    public CollectionAccessEventArgs(string collectionName, Type entityType, Type collectionType)
    {
        CollectionName = collectionName;
        EntityType = entityType;
        CollectionType = collectionType;
    }

    public string CollectionName { get; }
    public Type EntityType { get; }
    public Type CollectionType { get; }
}