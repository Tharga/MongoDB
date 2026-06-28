using System;
using ConsoleSample;
using ConsoleSample.DynamicRepo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tharga.Console;
using Tharga.Console.Commands;
using Tharga.Console.Consoles;
using Tharga.MongoDB;
using Tharga.MongoDB.Monitor.Client;

using var console = new ClientConsole();

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Sample diagnostic file log. Tharga.* at Trace so the full monitor/communication flow is captured.
var clientLogPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tharga-monitor-console.log");
builder.Logging.AddProvider(new ConsoleSample.FileLoggerProvider(clientLogPath));
builder.Logging.AddFilter<ConsoleSample.FileLoggerProvider>(null, LogLevel.Information);
builder.Logging.AddFilter<ConsoleSample.FileLoggerProvider>("Tharga", LogLevel.Trace);
// Tharga.Communication logs every forwarded message at Trace (one per second); keep it at Debug so the
// per-message flood stays out of the log while Tharga.MongoDB.* remains at Trace.
builder.Logging.AddFilter<ConsoleSample.FileLoggerProvider>("Tharga.Communication", LogLevel.Debug);
builder.Logging.AddFilter<ConsoleSample.FileLoggerProvider>("Microsoft", LogLevel.Warning);
Console.WriteLine($"[sample] File logging to {clientLogPath}");

builder.Services.AddMongoDB(builder.Configuration, o =>
{
    o.Monitor.Enabled = true;
    o.Monitor.EnableCommandMonitoring = true;
});

builder.AddMongoDbMonitorClient(sendTo: "https://localhost:7205");

var host = builder.Build();

host.UseMongoDB();

await using var scope = host.Services.CreateAsyncScope();
var sp = scope.ServiceProvider;

var resolver = new CommandResolver(type =>
{
    var instance = ActivatorUtilities.CreateInstance(sp, type);
    return instance as ICommand ?? throw new InvalidOperationException($"{type.FullName} must implement ICommand.");
});

var command = new RootCommand(console, resolver);

command.RegisterCommand<SampleCommands>();
command.RegisterCommand<DynamicCommands>();

var engine = new CommandEngine(command);

await host.StartAsync();

try
{
    engine.Start(args);
}
finally
{
    await host.StopAsync();
}
