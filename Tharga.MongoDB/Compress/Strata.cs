//using System;

//namespace Tharga.MongoDB.Compress;

//public record Strata
//{
//    private Strata()
//    {
//    }

//    public static Strata StartWith(CompressGranularity compressPer)
//    {
//        if (compressPer == CompressGranularity.None) throw new ArgumentException($"Cannot provide the value {compressPer} for {nameof(compressPer)}. You might not want a start value for this type.");

//        return new Strata
//        {
//            WhenOlderThan = CompressGranularity.None,
//            CompressPer = compressPer
//        };
//    }

//    public static Strata Compress(CompressGranularity whenOlderThan, CompressGranularity compressPer)
//    {
//        if (whenOlderThan == CompressGranularity.Drop) throw new ArgumentException($"Cannot provide the value {whenOlderThan} for {nameof(whenOlderThan)}.");
//        if (compressPer == CompressGranularity.None) throw new ArgumentException($"Cannot provide the value {compressPer} for {nameof(compressPer)}.");

//        return new Strata
//        {
//            WhenOlderThan = whenOlderThan,
//            CompressPer = compressPer
//        };
//    }

//    public static Strata Drop(CompressGranularity whenOlderThan)
//    {
//        if (whenOlderThan == CompressGranularity.Drop) throw new ArgumentException($"Cannot provide the value {whenOlderThan} for {nameof(whenOlderThan)}.");

//        return new Strata
//        {
//            WhenOlderThan = whenOlderThan,
//            CompressPer = CompressGranularity.Drop
//        };
//    }

//    public CompressGranularity WhenOlderThan { get; private init; }
//    public CompressGranularity CompressPer { get; private init; }
//}