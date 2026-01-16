namespace Tharga.MongoDB;

public record ExecuteLimiterOptions
{
    //TODO: This should be configurable from AddMongoDB or appsettings.json 
    public int MaxConcurrent { get; set; } = 20;
}