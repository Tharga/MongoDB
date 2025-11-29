using System;
using Tharga.MongoDB.Disk;

namespace Tharga.MongoDB;

public class CallStartEventArgs : EventArgs
{
    public CallStartEventArgs(Guid callKey, string collectionName, string functionName, Operation operation)
    {
        CallKey = callKey;
        CollectionName = collectionName;
        FunctionName = functionName;
        Operation = operation;
    }

    public Guid CallKey { get; }
    public string CollectionName { get; }
    public string FunctionName { get; }
    public Operation Operation { get; }
}