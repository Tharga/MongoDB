using System;

namespace Tharga.MongoDB;

public record TimeSeriesEntityBase<TKey> : EntityBase<TKey>
{
    public DateTime Timestamp { get; set; } //TODO: Change to init
}