namespace Tharga.MongoDB;

public record ExecuteLimiterOptions
{
    public int MaxConcurrent { get; set; } = 20;
}