using MongoDB.Bson.Serialization;
using System;

namespace Tharga.MongoDB;

/// <summary>
/// Overrides the global GuidStorageFormat for a single property.
/// The serializer will still read all three formats (Standard, CSharpLegacy, String),
/// but will write using the specified format.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class FlexibleGuidAttribute : Attribute, IBsonMemberMapAttribute
{
    public FlexibleGuidAttribute(GuidStorageFormat writeFormat = GuidStorageFormat.Standard)
    {
        WriteFormat = writeFormat;
    }

    public GuidStorageFormat WriteFormat { get; }

    public void Apply(BsonMemberMap memberMap)
        => memberMap.SetSerializer(new FlexibleGuidSerializer(WriteFormat));
}
