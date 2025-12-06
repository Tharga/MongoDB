using System;

namespace Tharga.MongoDB;

public class IndexUpdatedEventArgs : EventArgs
{
    public IndexUpdatedEventArgs(CollectionFingerprint fingerprint)
    {
        Fingerprint = fingerprint;
    }

    public CollectionFingerprint Fingerprint { get; }
}