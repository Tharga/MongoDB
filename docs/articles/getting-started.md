# Getting started

## Install

```
dotnet add package Tharga.MongoDB
```

## Register

```csharp
builder.AddMongoDB();
```

Or, if you only have access to `IServiceCollection`:

```csharp
builder.Services.AddMongoDB();
```

## Configure connection

In `appsettings.json`:

```json
"ConnectionStrings": {
  "Default": "mongodb://localhost:27017/MyApp{Environment}{Part}"
}
```

The `{Environment}` and `{Part}` placeholders are resolved per call from a `DatabaseContext`, so the same code can talk to multiple tenant databases without re-registering anything in DI.

## Define an entity

```csharp
public record WeatherForecast : EntityBase
{
    public DateOnly Date { get; set; }
    public int TemperatureC { get; set; }
    public string Summary { get; set; }
}
```

`EntityBase` defaults the key type to `ObjectId`. Use `EntityBase<TKey>` for `Guid`, `string`, `int`, etc.

## Define a collection

```csharp
public interface IWeatherForecastRepositoryCollection : IDiskRepositoryCollection<WeatherForecast> { }

internal class WeatherForecastRepositoryCollection
    : DiskRepositoryCollectionBase<WeatherForecast>, IWeatherForecastRepositoryCollection
{
    public WeatherForecastRepositoryCollection(IMongoDbServiceFactory factory)
        : base(factory) { }
}
```

The collection is registered in DI automatically — by default `AddMongoDB()` scans assemblies whose name starts with the entry-point assembly's prefix. Add external assemblies via `o.AddAutoRegistrationAssembly(...)`.

## Inject and use it

```csharp
public class WeatherForecastService
{
    private readonly IWeatherForecastRepositoryCollection _collection;

    public WeatherForecastService(IWeatherForecastRepositoryCollection collection)
    {
        _collection = collection;
    }

    public IAsyncEnumerable<WeatherForecast> GetAsync()
        => _collection.GetAsync();

    public Task AddAsync(WeatherForecast forecast)
        => _collection.AddAsync(forecast);
}
```

## Where next

- **[Lockable collections](lockable-collections.md)** — when you need optimistic per-document locks
- **[Transactions](transactions.md)** — multi-document atomicity
- **[Keyset pagination](keyset-pagination.md)** — for grids and large result sets
- **[Monitoring](monitoring.md)** — see every database call live
- Full configuration reference, customisation hooks, and Atlas-specific guidance: [README on GitHub](https://github.com/Tharga/MongoDB#readme)
