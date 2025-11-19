using System;
using System.Linq;

namespace Tharga.MongoDB;

public record IndexMeta
{
    public required string Name { get; init; }
    public required string[] Fields { get; init; }
    public required bool IsUnique { get; init; }

    public virtual bool Equals(IndexMeta other)
    {
        if (other is null) return false;
        return Name == other.Name
               && IsUnique == other.IsUnique
               && Fields.SequenceEqual(other.Fields);
    }

    public override int GetHashCode()
        => HashCode.Combine(Name, IsUnique,
            Fields.Aggregate(0, HashCode.Combine));
}