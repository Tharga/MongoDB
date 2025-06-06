using Tharga.MongoDB;

namespace HostSample.Features.DiskRepo;

public record WeatherForecast : EntityBase
{
    public DateOnly Date { get; set; }
    public int TemperatureC { get; set; }
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
    public string Summary { get; set; }
    public Guid SomeGuid { get; set; }
}