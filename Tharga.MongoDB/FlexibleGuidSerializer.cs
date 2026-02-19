using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using System;
using System.Threading;

namespace Tharga.MongoDB;

/// <summary>
/// A Guid serializer that can read all three storage formats (Standard, CSharpLegacy, String)
/// and writes in the configured format (default: Standard).
/// </summary>
public class FlexibleGuidSerializer : SerializerBase<Guid>
{
    private static readonly AsyncLocal<string> _collectionContext = new();

    private readonly GuidStorageFormat _writeFormat;

    internal static ILogger Logger { get; set; }

    /// <summary>
    /// Sets the collection name for the current async flow so that
    /// deserialization warnings can include it in log messages.
    /// </summary>
    internal static string CollectionContext
    {
        get => _collectionContext.Value;
        set => _collectionContext.Value = value;
    }

    public FlexibleGuidSerializer() : this(GuidStorageFormat.Standard) { }

    public FlexibleGuidSerializer(GuidStorageFormat writeFormat)
    {
        _writeFormat = writeFormat;
    }

    public override Guid Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        var bsonType = context.Reader.CurrentBsonType;
        return bsonType switch
        {
            BsonType.String => DeserializeString(context.Reader.ReadString()),
            BsonType.Binary => DeserializeBinary(context.Reader.ReadBinaryData()),
            _ => throw new BsonSerializationException($"Cannot deserialize Guid from BsonType.{bsonType}.")
        };
    }

    private Guid DeserializeString(string value)
    {
        var guid = Guid.Parse(value);

        if (_writeFormat != GuidStorageFormat.String)
        {
            Logger?.LogWarning("Guid {Guid} was read as String but the configured write format is {WriteFormat} in collection {Collection}. Query filters may not match this document.", guid, _writeFormat, CollectionContext ?? "unknown");
        }

        return guid;
    }

    private Guid DeserializeBinary(BsonBinaryData binary)
    {
        return binary.SubType switch
        {
            BsonBinarySubType.UuidLegacy => DeserializeLegacy(binary),
            BsonBinarySubType.UuidStandard => DeserializeStandard(binary),
            _ => throw new BsonSerializationException($"Cannot deserialize Guid from binary subtype {binary.SubType}.")
        };
    }

    private Guid DeserializeLegacy(BsonBinaryData binary)
    {
        var guid = binary.ToGuid(GuidRepresentation.CSharpLegacy);

        if (_writeFormat != GuidStorageFormat.CSharpLegacy)
        {
            Logger?.LogWarning("Guid {Guid} was read as CSharpLegacy (BSON subtype 3) but the configured write format is {WriteFormat} in collection {Collection}. Query filters may not match this document.", guid, _writeFormat, CollectionContext ?? "unknown");
        }

        return guid;
    }

    private Guid DeserializeStandard(BsonBinaryData binary)
    {
        var guid = binary.ToGuid(GuidRepresentation.Standard);

        if (_writeFormat != GuidStorageFormat.Standard)
        {
            Logger?.LogWarning("Guid {Guid} was read as Standard (BSON subtype 4) but the configured write format is {WriteFormat} in collection {Collection}. Query filters may not match this document.", guid, _writeFormat, CollectionContext ?? "unknown");
        }

        return guid;
    }

    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, Guid value)
    {
        switch (_writeFormat)
        {
            case GuidStorageFormat.String:
                context.Writer.WriteString(value.ToString());
                break;
            case GuidStorageFormat.CSharpLegacy:
                context.Writer.WriteBinaryData(new BsonBinaryData(value, GuidRepresentation.CSharpLegacy));
                break;
            case GuidStorageFormat.Standard:
            default:
                context.Writer.WriteBinaryData(new BsonBinaryData(value, GuidRepresentation.Standard));
                break;
        }
    }
}
