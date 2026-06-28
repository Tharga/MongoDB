using Blazored.LocalStorage;
using Microsoft.Extensions.Logging;
using Radzen;
using Tharga.Blazor.Framework;
using Tharga.MongoDB;
using Tharga.MongoDB.Configuration;
using Tharga.Mcp;
using Tharga.MongoDB.Mcp;
using Tharga.MongoDB.Monitor.Server;
using Tharga.TemplateBlazor.Web.Components;
using Tharga.TemplateBlazor.Web.Framework;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

builder.Services.AddBlazoredLocalStorage();
builder.Services.AddRadzenComponents();
builder.Services.AddRadzenCookieThemeService(options =>
{
    options.Name = Constants.ThemeStorageName;
    options.Duration = TimeSpan.FromDays(365);
});

builder.Services.AddThargaBlazor(o =>
{
    o.Title = "Tharga Template Site";
});

builder.AddMongoDB(o =>
{
    o.DefaultConfigurationName = "Core";
    o.AssureIndex = AssureIndexMode.BySchema;
    o.Monitor.StorageMode = MonitorStorageMode.Database;
    o.Monitor.EnableCommandMonitoring = true;
});

builder.AddMongoDbMonitorServer(_ => { });

// Sample diagnostic file log. Tharga.* at Trace so the full monitor/communication flow is captured.
var serverLogPath = Path.Combine(Path.GetTempPath(), "tharga-monitor-server.log");
builder.Logging.AddProvider(new Tharga.TemplateBlazor.Web.FileLoggerProvider(serverLogPath));
builder.Logging.AddFilter<Tharga.TemplateBlazor.Web.FileLoggerProvider>(null, LogLevel.Information);
builder.Logging.AddFilter<Tharga.TemplateBlazor.Web.FileLoggerProvider>("Tharga", LogLevel.Trace);
// Tharga.Communication logs every forwarded message at Trace (one per second per agent); keep it at
// Debug so the per-message flood stays out of the log while Tharga.MongoDB.* remains at Trace.
builder.Logging.AddFilter<Tharga.TemplateBlazor.Web.FileLoggerProvider>("Tharga.Communication", LogLevel.Debug);
builder.Logging.AddFilter<Tharga.TemplateBlazor.Web.FileLoggerProvider>("Microsoft", LogLevel.Warning);
Console.WriteLine($"[sample] File logging to {serverLogPath}");

builder.Services.AddThargaMcp(mcp =>
{
    mcp.Options.RequireAuth = false;
    mcp.AddMongoDB();
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("MonitorHub", policy =>
    {
        policy.AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials()
              .SetIsOriginAllowed(_ => true);
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseCors("MonitorHub");
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(Tharga.TemplateBlazor.Web.Client._Imports).Assembly);

app.UseMongoDB();
app.UseMongoDbMonitorServer();

app.UseThargaMcp();

app.MapGet("/api/monitor/clients", async (MonitorClientStateService svc) =>
{
    var clients = new List<object>();
    await foreach (var c in svc.GetAsync())
    {
        clients.Add(new { c.Instance, c.ConnectionId, c.Machine, c.Type, c.Version, c.IsConnected, c.ConnectTime, c.DisconnectTime });
    }
    return clients;
});

app.MapGet("/api/monitor/remote-calls", (IDatabaseMonitor monitor) =>
{
    var calls = monitor.GetCallDtos(CallType.Last)
        .Where(c => c.SourceName != null && !c.SourceName.Contains("Tharga.TemplateBlazor"))
        .Take(20);
    return calls;
});

app.MapGet("/api/monitor/all-calls", (IDatabaseMonitor monitor) =>
{
    return monitor.GetCallDtos(CallType.Last).Take(20);
});

app.Run();
