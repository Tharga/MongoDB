using System;
using Tharga.MongoDB.Disk;

namespace Tharga.MongoDB;

public class CallStartEventArgs : EventArgs
{
    public CallStartEventArgs(Guid callKey, CollectionFingerprint fingerprint, string functionName, Operation operation)
    {
        CallKey = callKey;
        Fingerprint = fingerprint;
        FunctionName = functionName;
        Operation = operation;
    }

    public Guid CallKey { get; }
    public CollectionFingerprint Fingerprint { get; }
    public string FunctionName { get; }
    public Operation Operation { get; }
}