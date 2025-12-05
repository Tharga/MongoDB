using System;
using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB;

public record CollectionFingerprint //: ICollectionFingerprint //: IDatabaseContext
{
    private readonly ConfigurationName _configurationName;
    private string _key;

    public required ConfigurationName ConfigurationName
    {
        get => _configurationName;
        init => _configurationName = value ?? throw new NullReferenceException($"{nameof(ConfigurationName)} cannot be null in {nameof(CollectionFingerprint)}.");
    }

    public required string DatabaseName { get; init; }
    public required string CollectionName { get; init; }

    public string Key => _key ??= $"{ConfigurationName.Value}.{DatabaseName}.{CollectionName}";
}