namespace Tharga.MongoDB.Compress;

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