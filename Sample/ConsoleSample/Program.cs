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

builder.Services.AddMongoDB(builder.Configuration, o =>
{
    o.Monitor.Enabled = true;
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
