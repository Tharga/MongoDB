using System;

namespace Tharga.MongoDB;

public class CollectionAccessEventArgs : EventArgs
{
    public CollectionAccessEventArgs(CollectionFingerprint fingerprint, string server, Type entityType, string databasePart)
    {
        Fingerprint = fingerprint;
        Server = server;
        EntityType = entityType;
        DatabasePart = databasePart;
    }

    public CollectionFingerprint Fingerprint { get;  }
    public string Server { get; }
    public Type EntityType { get; }
    public string DatabasePart { get; }
}