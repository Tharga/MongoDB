using System;
using System.Collections.Generic;
using System.Linq;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Tharga.MongoDB.Compress;

public abstract record CompressEntityBase<TEntity, TKey> : EntityBase<TKey>
    where TEntity : EntityBase<TKey>
{
    public DateTime? Timestamp { get; init; }

    [BsonElement(nameof(AggregateKey))]
    public abstract string AggregateKey { get; }

    [BsonElement(nameof(Granularity))]
    [BsonRepresentation(BsonType.String)]
    public CompressGranularity Granularity { get; internal set; }

    public abstract TEntity Merge(TEntity other);
    public virtual IEnumerable<Strata> GetStratas() => null;

    public Strata GetStrata()
    {
        var age = GetAge(Timestamp);

        var stratas = GetStratas().ToArray()
            .Where(x => age >= x.WhenOlderThan)
            .OrderBy(x => x.WhenOlderThan);

        return stratas.FirstOrDefault();
    }

    private CompressGranularity GetAge(DateTime? time)
    {
        if (time == null) return CompressGranularity.None;

        var age = time.Value.ToUniversalTime() - DateTime.UtcNow;
        var a = CompressGranularity.None;
        if (age.TotalDays * 31 > 1)
            a = CompressGranularity.Month;
        if (age.TotalDays > 1)
            a = CompressGranularity.Day;
        if (age.TotalHours > 1)
            a = CompressGranularity.Hour;
        else if (age.TotalMinutes > 1)
            a = CompressGranularity.Minute;
        return a;
    }
}

public enum CompressGranularity
{
    None,
    Minute,
    Hour,
    Day,
    Month,
    Year
}

public record Strata
{
    public Strata(CompressGranularity startWith)
    {
        WhenOlderThan = CompressGranularity.None;
        CompressPer = startWith;
    }

    public Strata(CompressGranularity whenOlderThan, CompressGranularity compressPer)
    {
        WhenOlderThan = whenOlderThan;
        CompressPer = compressPer;
    }

    public CompressGranularity WhenOlderThan { get; }
    public CompressGranularity CompressPer { get; }
}