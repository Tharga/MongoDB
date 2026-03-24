# Documentation Requests

## 2026-03-24 — Clarify auto-registration of repositories/collections from external assemblies

**Requested by:** Eplicta project (Eplicta.Aggregator), via Claude Code session with Daniel Bohlin

### Problem

When consuming a NuGet package that contains internal MongoDB repository collections (e.g. `Tharga.Team.Service` which has `ApiKeyRepositoryCollection` and `ApiKeyRepository`), the auto-registration in `AddMongoDB()` does not discover them. This causes a runtime `System.AggregateException` at `builder.Build()`:

```
Unable to resolve service for type 'Tharga.Team.Service.IApiKeyRepository'
while attempting to activate 'Tharga.Team.Service.ApiKeyAdministrationService'.
```

The root cause is that `AutoRegistrationAssemblies` (set via `AssemblyService.GetAssemblies<Program>()`) scans assemblies matching the caller's namespace prefix. External packages like `Tharga.Team.Service` live under a different namespace (`Tharga.Team.Service`) and are not included in the scan. The developer must call `AddAutoRegistrationAssembly()` to include them, but this requirement is not documented.

### What's missing from the documentation

1. **MongoDB README.md** — Currently says "The repositories and collections are registered in the IOC automatically" but does not mention that this only applies to assemblies within the scan scope. There is no mention of `AddAutoRegistrationAssembly()` at all.

2. **No guidance for NuGet package consumers** — When a third-party or sibling package (like `Tharga.Team.Service`) contains `internal` classes that inherit from `DiskRepositoryCollectionBase<T>` or implement `IRepository`, the consumer has no way to know they need to register that assembly manually.

3. **No guidance for NuGet package authors** — Packages that ship MongoDB collections should either:
   - Document that consumers must call `o.AddAutoRegistrationAssembly(typeof(SomePublicType).Assembly)` in their `AddMongoDB()` setup, or
   - Handle it internally (the way `Tharga.Cache.MongoDB` does in its `DatabaseOptionsExtensions.AddCache()` method)

### Suggested documentation additions

#### In the MongoDB README.md

Add a section explaining:

- `AutoRegistrationAssemblies` controls which assemblies are scanned (default: assemblies matching the entry point's namespace prefix)
- `AddAutoRegistrationAssembly(Assembly)` adds additional assemblies to the scan
- When using NuGet packages that contain MongoDB repository collections, you must add their assembly explicitly:

```csharp
builder.AddMongoDB(o =>
{
    o.AutoRegistrationAssemblies = AssemblyService.GetAssemblies<Program>();
    o.AddAutoRegistrationAssembly(typeof(SomeTypeFromPackage).Assembly);
});
```

#### In the XML doc comments

The `AutoRegistrationAssemblies` property comment currently says:
> "Assemblies that starts with the same name are registered automatically."

This should also mention that assemblies from external packages are NOT included by default and must be added via `AddAutoRegistrationAssembly()`.

### Existing internal example

`Tharga.Cache.MongoDB` already handles this correctly in `DatabaseOptionsExtensions.cs`:

```csharp
public static void AddCache(this DatabaseOptions options)
{
    options.AddAutoRegistrationAssembly(Assembly.GetAssembly(typeof(IMongoDB)));
}
```

This pattern could be referenced as the recommended approach for package authors.

### The fix we applied in Eplicta.Aggregator

```csharp
builder.AddMongoDB(o =>
{
    o.DefaultConfigurationName = "Aggregator";
    o.AutoRegistrationAssemblies = AssemblyService.GetAssemblies<Program>();
    o.AddAutoRegistrationAssembly(typeof(ApiKeyConstants).Assembly); // Tharga.Team.Service
    // ...
});
```

### Related note for Tharga.Team.Service

`AddThargaApiKeys()` in `ControllersRegistration.cs` calls `ApiKeyServiceRegistration.RegisterApiKeyService()` which registers `IApiKeyService` but does NOT register `IApiKeyRepository` or `IApiKeyRepositoryCollection`. It relies on MongoDB auto-registration to discover them, but never tells the consumer to add the assembly. Consider either:
- Adding a note in the Team.Service README/implementation-guide, or
- Having `AddThargaApiKeys()` accept and call `DatabaseOptions.AddAutoRegistrationAssembly()` itself (like `Tharga.Cache.MongoDB` does)
