using System;

namespace Tharga.MongoDB;

public record CollectionStats
{
    public long DocumentCount { get; init; }
    public long Size { get; init; }
    public DateTime? UpdatedAt { get; init; }
}
