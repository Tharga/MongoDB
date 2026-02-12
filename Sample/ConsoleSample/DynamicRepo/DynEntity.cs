using System;
using Tharga.MongoDB;

namespace ConsoleSample.DynamicRepo;

public record DynEntity : EntityBase
{
  // Uses whatever GuidStorageFormat is configured globally (default: Standard)
  public Guid StandardKey { get; init; }

  // Overrides the global setting for this property only — always stored as CSharpLegacy
  [FlexibleGuid(GuidStorageFormat.CSharpLegacy)]
  public Guid LegacyKey { get; init; }
}