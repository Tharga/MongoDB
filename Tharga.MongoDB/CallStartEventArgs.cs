using System;
using Tharga.MongoDB.Disk;

namespace Tharga.MongoDB;

public class CallStartEventArgs : EventArgs
{
    public CallStartEventArgs(Guid callKey, CollectionFingerprint fingerprint, string functionName, Operation operation, string sourceName = null)
    {
        CallKey = callKey;
        Fingerprint = fingerprint;
        FunctionName = functionName;
        Operation = operation;
        SourceName = sourceName;
    }

    public Guid CallKey { get; }
    public CollectionFingerprint Fingerprint { get; }
    public string FunctionName { get; }
    public Operation Operation { get; }
    public string SourceName { get; }
}