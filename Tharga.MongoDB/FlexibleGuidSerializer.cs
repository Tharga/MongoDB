using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using System;

namespace Tharga.MongoDB;

/// <summary>
/// A Guid serializer that can read all three storage formats (Standard, CSharpLegacy, String)
/// and writes in the configured format (default: Standard).
/// </summary>
public class FlexibleGuidSerializer : SerializerBase<Guid>
{
    private readonly GuidStorageFormat _writeFormat;

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
            BsonType.String => Guid.Parse(context.Reader.ReadString()),
            BsonType.Binary => DeserializeBinary(context.Reader.ReadBinaryData()),
            _ => throw new BsonSerializationException($"Cannot deserialize Guid from BsonType.{bsonType}.")
        };
    }

    private static Guid DeserializeBinary(BsonBinaryData binary)
    {
        return binary.SubType switch
        {
            BsonBinarySubType.UuidLegacy   => binary.ToGuid(GuidRepresentation.CSharpLegacy),
            BsonBinarySubType.UuidStandard => binary.ToGuid(GuidRepresentation.Standard),
            _ => throw new BsonSerializationException($"Cannot deserialize Guid from binary subtype {binary.SubType}.")
        };
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
