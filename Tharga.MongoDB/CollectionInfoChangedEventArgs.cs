using System;

namespace Tharga.MongoDB;

public class ExecuteInfoChangedEventArgs : EventArgs
{
}

public class CollectionInfoChangedEventArgs : EventArgs
{
    public CollectionInfoChangedEventArgs(CollectionInfo collectionInfo)
    {
        CollectionInfo = collectionInfo;
    }

    public CollectionInfo CollectionInfo { get; }
}