# Requests

## Pending

### Publish Tharga.MongoDB with IHostApplicationBuilder overload
- **From:** Tharga.Starter (`c:\dev\tharga\Starter`)
- **Date:** 2026-03-24
- **Priority:** High
- **Description:** The `AddMongoDB` extension currently only has an `IServiceCollection` overload in the published NuGet (2.7.8). The local source has an `IHostApplicationBuilder` overload. This is needed so `builder.AddMongoDB()` works consistently. Also, `Tharga.Team.MongoDB 2.0.1` depends on `Tharga.MongoDB >= 2.8.5-pre.20` which is not published as stable — a stable release of Tharga.MongoDB >= 2.8.5 is needed.
- **Status:** Done (2026-03-25) — README updated, version bumped to 2.9, release gate restored to master-only
