using ConsoleSample;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Linq;
using Tharga.Console;
using Tharga.Console.Commands;
using Tharga.Console.Consoles;
using Tharga.MongoDB;
using Tharga.Runtime;

var environmentName =
    Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
    ?? Environments.Production;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddEnvironmentVariables()
    .Build();

var serviceCollection = new ServiceCollection();
serviceCollection.AddSingleton<IConfiguration>(configuration);
serviceCollection.AddSingleton<IHostEnvironment>(sp =>
{
    var env = new HostEnvironment
    {
        EnvironmentName = environmentName,
        ApplicationName = AppDomain.CurrentDomain.FriendlyName,
        ContentRootPath = AppContext.BaseDirectory
    };

    env.ContentRootFileProvider =
        new Microsoft.Extensions.FileProviders.PhysicalFileProvider(env.ContentRootPath);

    return env;
});
_ = AssemblyService.GetTypes<ICommand>().Select(serviceCollection.AddTransient).ToArray();
serviceCollection.AddMongoDB();
var serviceProvider = serviceCollection.BuildServiceProvider();

using var console = new ClientConsole();
var command = new RootCommand(console, new CommandResolver(type => (ICommand)serviceProvider.GetService(type)));
command.RegisterCommand<SampleCommands>();
var engine = new CommandEngine(command);
engine.Start(args);

//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Threading.Tasks;
//using ConsoleSample.SampleRepo;
//using Microsoft.Extensions.DependencyInjection;
//using Microsoft.Extensions.Hosting;
//using Microsoft.Extensions.Logging;
//using Tharga.MongoDB;
//using Tharga.MongoDB.Configuration;

//namespace ConsoleSample;

//internal static class Program
//{
//    private static async Task Main(string[] args)
//    {
//        var host = Host.CreateDefaultBuilder(args)
//            .ConfigureServices((_, services) =>
//            {
//                //services.AddMongoDB(o =>
//                //{
//                //    o.AssureIndex = AssureIndexMode.DropCreate;

//                //    o.ConnectionStringLoader = (ConfigurationName _, IServiceProvider _) => Task.FromResult<ConnectionString>("mongodb://localhost:27017/Tharga_MongoDB_ConsoleSample{part}");

//                //    o.ActionEvent = data => { Console.WriteLine($"---> {data.Action.Message}"); };

//                //    o.ConfigurationLoader = (IServiceProvider _) => Task.FromResult(new MongoDbConfigurationTree
//                //    {
//                //        Configurations = new Dictionary<ConfigurationName, MongoDbConfiguration>
//                //        {
//                //            {
//                //                "Default", new MongoDbConfiguration
//                //                {
//                //                    AccessInfo = new MongoDbApiAccess
//                //                    {
//                //                        PublicKey = "[PublicKey]",
//                //                        PrivateKey = "[PrivateKey]",
//                //                        GroupId = "[GroupId]"
//                //                    },
//                //                    ResultLimit = 100,
//                //                    AutoClean = true,
//                //                    CleanOnStartup = true,
//                //                    CreateCollectionStrategy = CreateStrategy.DropEmpty
//                //                }
//                //            },
//                //            {
//                //                "Other", new MongoDbConfiguration
//                //                {
//                //                    ResultLimit = 200
//                //                }
//                //            }
//                //        },
//                //        ResultLimit = 1000,
//                //        AutoClean = false,
//                //        CleanOnStartup = false,
//                //        CreateCollectionStrategy = CreateStrategy.DropEmpty
//                //    });
//                //});

//                //services.AddLogging(x =>
//                //{
//                //    x.AddConsole();
//                //    x.SetMinimumLevel(LogLevel.Trace);
//                //});
//            })
//            .Build();

//        //var allMulti = await AccessMultiRepo(host.Services);

//        //Console.WriteLine($"Exiting with {allMulti.Length} items in database.");
//    }

//    private static async Task<MyEntity[]> AccessMultiRepo(IServiceProvider serviceProvider)
//    {
//        var multiRepository = serviceProvider.GetService<IMultiRepository>();
//        var items = await multiRepository.GetAll().ToArrayAsync();
//        return items;
//    }
//}