using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ConsoleSample.SampleRepo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tharga.MongoDB;
using Tharga.MongoDB.Configuration;

namespace ConsoleSample;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices((_, services) =>
            {
                services.AddMongoDB(o =>
                {
                    o.AssureIndex = AssureIndexMode.DropCreate;

                    o.ConnectionStringLoader = (ConfigurationName _, IServiceProvider _) => Task.FromResult<ConnectionString>("mongodb://localhost:27017/Tharga_MongoDB_ConsoleSample{part}");

                    o.ActionEvent = data => { Console.WriteLine($"---> {data.Action.Message}"); };

                    o.ConfigurationLoader = (IServiceProvider _) => Task.FromResult(new MongoDbConfigurationTree
                    {
                        Configurations = new Dictionary<ConfigurationName, MongoDbConfiguration>
                        {
                            {
                                "Default", new MongoDbConfiguration
                                {
                                    AccessInfo = new MongoDbApiAccess
                                    {
                                        PublicKey = "[PublicKey]",
                                        PrivateKey = "[PrivateKey]",
                                        GroupId = "[GroupId]"
                                    },
                                    ResultLimit = 100,
                                    AutoClean = true,
                                    CleanOnStartup = true,
                                    CreateCollectionStrategy = CreateStrategy.DropEmpty
                                }
                            },
                            {
                                "Other", new MongoDbConfiguration
                                {
                                    ResultLimit = 200
                                }
                            }
                        },
                        ResultLimit = 1000,
                        AutoClean = false,
                        CleanOnStartup = false,
                        CreateCollectionStrategy = CreateStrategy.DropEmpty
                    });
                });

                services.AddLogging(x =>
                {
                    x.AddConsole();
                    x.SetMinimumLevel(LogLevel.Trace);
                });
            })
            .Build();

        var allMulti = await AccessMultiRepo(host.Services);

        Console.WriteLine($"Exiting with {allMulti.Length} items in database.");
    }

    private static async Task<MyEntity[]> AccessMultiRepo(IServiceProvider serviceProvider)
    {
        var multiRepository = serviceProvider.GetService<IMultiRepository>();
        var items = await multiRepository.GetAll().ToArrayAsync();
        return items;
    }
}