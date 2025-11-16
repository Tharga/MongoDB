using System;
using MongoDB.Driver;
using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB;

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

public class CollectionAccessEventArgs : EventArgs
{
    public CollectionAccessEventArgs(ConfigurationName configurationName, string collectionName, Type entityType, Type collectionType)
    {
        ConfigurationName = configurationName;
        CollectionName = collectionName;
        EntityType = entityType;
        CollectionType = collectionType;
    }

    public ConfigurationName ConfigurationName { get; }
    public string CollectionName { get; }
    public Type EntityType { get; }
    public Type CollectionType { get; }
}