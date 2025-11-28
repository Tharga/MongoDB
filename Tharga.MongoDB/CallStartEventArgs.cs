using System;

namespace Tharga.MongoDB;

public class CallStartEventArgs : EventArgs
{
    public CallStartEventArgs(Guid callKey, string collectionName, string functionName)
    {
        CallKey = callKey;
        CollectionName = collectionName;
        FunctionName = functionName;
    }

    public Guid CallKey { get; }
    public string CollectionName { get; }
    public string FunctionName { get; }
}