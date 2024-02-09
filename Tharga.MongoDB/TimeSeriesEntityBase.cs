using System;

namespace Tharga.MongoDB;

public record TimeSeriesEntityBase<TMetadata, TKey> : EntityBase<TKey>
{
    public DateTime Timestamp { get; set; }
    public TMetadata Metadata { get; set; }
}