using ConsoleSample;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using ConsoleSample.DynamicRepo;
using Microsoft.Extensions.Logging;
using Tharga.Console;
using Tharga.Console.Commands;
using Tharga.Console.Consoles;
using Tharga.MongoDB;

using var console = new ClientConsole();

var host = Host.CreateDefaultBuilder(args)
    .ConfigureHostConfiguration(builder =>
    {
        builder.AddEnvironmentVariables();
    })
    .ConfigureAppConfiguration((_, builder) =>
    {
        builder
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddEnvironmentVariables();
    })
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddConsole();
    })
    .ConfigureServices((context, services) =>
    {
        //_ = AssemblyService.GetTypes<ICommand>().Select(services.AddTransient).ToArray();

        services.AddMongoDB(context.Configuration, o =>
        {
            o.Monitor.Enabled = true;
        });
    })
    .Build();

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