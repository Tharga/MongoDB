using System;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;
using Tharga.MongoDB;

namespace ConsoleSample.DynamicRepo;

public record DynEntity : EntityBase
{
  // Uses whatever GuidRepresentation is configured globally (e.g. Standard)
  public Guid StandardKey { get; init; }

  // Overrides the global setting for this property only — always stored as CSharpLegacy
  [BsonGuidRepresentation(GuidRepresentation.CSharpLegacy)]
  public Guid LegacyKey { get; init; }
}