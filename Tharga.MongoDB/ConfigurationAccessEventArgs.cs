using System;
using MongoDB.Driver;
using Tharga.MongoDB.Configuration;

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

public class CallEndEventArgs : EventArgs
{
    public CallEndEventArgs(Guid callKey, TimeSpan elapsed, Exception exception)
    {
        CallKey = callKey;
        Elapsed = elapsed;
        Exception = exception;
    }

    public Guid CallKey { get; }
    public TimeSpan Elapsed { get; }
    public Exception Exception { get; }
}

public class ConfigurationAccessEventArgs : EventArgs
{
    public ConfigurationAccessEventArgs(ConfigurationName configurationName, MongoUrl mongoUrl)
    {
        ConfigurationName = configurationName;
        MongoUrl = mongoUrl;
    }

    public ConfigurationName ConfigurationName { get; }
    public MongoUrl MongoUrl { get; }
}