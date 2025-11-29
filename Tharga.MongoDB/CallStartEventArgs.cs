using System;
using Tharga.MongoDB.Disk;

namespace Tharga.MongoDB;

public class CallStartEventArgs : EventArgs
{
    public CallStartEventArgs(Guid callKey, string configurationName, string databaseName, string collectionName, string functionName, Operation operation)
    {
        ConfigurationName = configurationName;
        DatabaseName = databaseName;
        CallKey = callKey;
        CollectionName = collectionName;
        FunctionName = functionName;
        Operation = operation;
    }

    public Guid CallKey { get; }
    public string ConfigurationName { get; init; }
    public string DatabaseName { get; init; }
    public string CollectionName { get; }
    public string FunctionName { get; }
    public Operation Operation { get; }
}