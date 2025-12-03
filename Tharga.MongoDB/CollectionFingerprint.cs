using System;
using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB;

public record CollectionFingerprint : IDatabaseContext
{
    public required ConfigurationName ConfigurationName { get; init; }
    public required string DatabaseName { get; init; }
    public required string CollectionName { get; init; }

    public virtual bool Equals(CollectionFingerprint other)
    {
        if (other?.ConfigurationName != ConfigurationName) return false;
        if (other?.DatabaseName != DatabaseName) return false;
        if (other?.CollectionName != CollectionName) return false;
        return true;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(ConfigurationName, DatabaseName, CollectionName);
    }
}