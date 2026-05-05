# Tharga.MongoDB.Blazor

Drop-in Blazor admin UI for [`Tharga.MongoDB`](https://www.nuget.org/packages/Tharga.MongoDB). Renders the live monitoring data already captured by the core package — collections, calls, indexes, queue metrics, connected clients — as Razor components you can host on any admin page in your app.

## Install

```
dotnet add package Tharga.MongoDB.Blazor
```

The components depend on the data flowing through `Tharga.MongoDB`'s `IDatabaseMonitor`, so you need that package registered as well:

```csharp
builder.AddMongoDB();
```

Then drop the components onto a Blazor page — they auto-discover the monitor via DI:

```razor
@page "/database"

<RadzenCard Style="margin-bottom: 6px;">
    <Tharga.MongoDB.Blazor.MonitorToolbar />
</RadzenCard>

<RadzenTabs>
    <Tabs>
        <RadzenTabsItem Text="Collections">
            <Tharga.MongoDB.Blazor.CollectionView />
        </RadzenTabsItem>
        <RadzenTabsItem Text="Calls">
            <Tharga.MongoDB.Blazor.CallView />
        </RadzenTabsItem>
        <RadzenTabsItem Text="Clients">
            <Tharga.MongoDB.Blazor.ClientsView />
        </RadzenTabsItem>
    </Tabs>
</RadzenTabs>
```

## Components

- **`<MonitorToolbar />`** — top-bar controls: configuration switcher, reset calls, reset cache.
- **`<CollectionView />`** — table of every registered collection with status, document count, indexes, and per-collection drill-down dialog.
- **`<CallView />`** — every database call captured by the monitor, with filter, sort, explain plan, and timing.
- **`<ClientsView />`** — connected MongoDB clients (driver-side). Useful with `Tharga.MongoDB.Monitor.Server` when aggregating multiple agents.
- **`<QueueView />`** — execute-limiter queue depth and throughput.
- **`<ConfigurationsSelector />`** — switch between configured `ConnectionStrings` entries when an app talks to multiple clusters.

The package is built on Radzen.Blazor — the same components and theming as the rest of your Radzen app.

## Documentation

Full docs and configuration reference: [github.com/Tharga/MongoDB](https://github.com/Tharga/MongoDB).

[![GitHub repo](https://img.shields.io/github/repo-size/Tharga/MongoDB?style=flat&logo=github&logoColor=red&label=Repo)](https://github.com/Tharga/MongoDB)
