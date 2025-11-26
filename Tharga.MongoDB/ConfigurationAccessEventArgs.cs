using System;
using MongoDB.Driver;
using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB;

public class CallStartEventArgs : EventArgs
{
    public CallStartEventArgs(Guid callKey, string collectionName, string functionName)
    {
        throw new NotImplementedException();
    }
}

public class CallEndEventArgs : EventArgs
{
    public CallEndEventArgs(Guid callKey, TimeSpan elapsed, Exception exception)
    {
        throw new NotImplementedException();
    }
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