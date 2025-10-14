using System;

namespace Tharga.MongoDB;

public record CollectionType
{
    public required Type ServiceType { get; init; }
    public required Type ImplementationType { get; init; }
    public required bool IsDynamic { get; init; }
}