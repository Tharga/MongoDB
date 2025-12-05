using System;

namespace Tharga.MongoDB;

public class CollectionInfoChangedEventArgs : EventArgs
{
    public CollectionInfoChangedEventArgs(CollectionInfo collectionInfo)
    {
        CollectionInfo = collectionInfo;
    }

    public CollectionInfo CollectionInfo { get; }
}