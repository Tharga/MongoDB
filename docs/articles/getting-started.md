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

### Exceptions you expect

The collection logs an exception from a database operation before it reaches your code — at Warning for connection, timeout and paused-pool errors, at Error for everything else. If you rely on a database error and handle it, such as a duplicate key from a unique index used to detect a lost race, override `GetExceptionLogLevel` so the handled case does not look like a fault. `LogLevel.None` skips the entry; the exception is still thrown and the call is still monitored.

```csharp
protected override LogLevel GetExceptionLogLevel(Exception exception, string operation)
{
    return exception is MongoWriteException { WriteError.Category: ServerErrorCategory.DuplicateKey }
        ? LogLevel.Information
        : base.GetExceptionLogLevel(exception, operation);
}
```

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

    public Task AddAsync(WeatherForecast forecast, CancellationToken cancellationToken = default)
        => _collection.AddAsync(forecast, cancellationToken: cancellationToken);
}
```

Every read and write method takes an optional `CancellationToken` as its last parameter (the writes since 2.19), and passes it to the driver.

## Where next

- **[Lockable collections](lockable-collections.md)** — when you need optimistic per-document locks
- **[Transactions](transactions.md)** — multi-document atomicity
- **[Keyset pagination](keyset-pagination.md)** — for grids and large result sets
- **[Monitoring](monitoring.md)** — see every database call live
- Full configuration reference, customisation hooks, and Atlas-specific guidance: [README on GitHub](https://github.com/Tharga/MongoDB#readme)
