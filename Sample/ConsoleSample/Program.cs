using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ConsoleSample.SampleRepo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tharga.MongoDB;
using Tharga.MongoDB.Configuration;

namespace ConsoleSample;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        var services = new ServiceCollection();

        //services.AddTransient<ISomeDependency, SomeDependency>();

        //services.AddTransient<ConnectionStringLoader>();
        //services.AddMongoDB(o =>
        //{
        //    o.ConnectionStringLoader = async (name, provider) => await provider.GetService<ConnectionStringLoader>().GetConnectionString(name);
        //});

        services.AddMongoDB(o =>
        {
            o.ConnectionStringLoader = (_, _) => Task.FromResult<ConnectionString>("mongodb://localhost:27017/Tharga_MongoDB_ConsoleSample{part}");
            o.ActionEvent = data => { Console.WriteLine($"---> {data.Action.Message}"); };
            o.ConfigurationLoader = _ => Task.FromResult(new MongoDbConfigurationTree
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
                            DropEmptyCollections = true
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
                DropEmptyCollections = false
            });
            //o.AutoRegisterCollections = true;
        });
        services.AddLogging(x =>
        {
            x.AddConsole();
            x.SetMinimumLevel(LogLevel.Trace);
        });

        var serviceProvider = services.BuildServiceProvider();
        var allMulti = await AccessMultiRepo(serviceProvider);

        Console.WriteLine($"Exiting with {allMulti.Length} items in database.");
    }

    private static async Task<MyEntity[]> AccessMultiRepo(ServiceProvider serviceProvider)
    {
        var multiRepository = serviceProvider.GetService<IMultiRepository>();
        var items = await multiRepository.GetAll().ToArrayAsync();
        return items;
    }

    private static async Task<MyBaseEntity[]> AccessBufferRepo(ServiceProvider serviceProvider)
    {
        var myRepo = serviceProvider.GetService<MySimpleBufferRepo>();
        var all1 = await myRepo.GetAsync(x => true).ToArrayAsync();
        await myRepo.AddAsync(new MyEntity { Value = "ABC123" });
        await myRepo.AddAsync(new MyOtherEntity { Value = "ABC123" });
        var all2 = await myRepo.GetAsync(x => true).ToArrayAsync();
        return all2;
    }

    //private static async Task<MyBaseEntity[]> AccessDiskRepo(ServiceProvider serviceProvider)
    //{
    //    var myRepo = serviceProvider.GetService<MySimpleDiskRepo>();
    //    var all1 = await myRepo.GetAsync(x => true).ToArrayAsync();
    //    await myRepo.AddAsync(new MyEntity { Value = "ABC123" });
    //    await myRepo.AddAsync(new MyOtherEntity { Value = "ABC123" });
    //    var all2 = await myRepo.GetAsync(x => true).ToArrayAsync();
    //    return all2;
    //}
}

//public class SomeDependency : ISomeDependency
//{
//    public Task<string> GetValueAsync()
//    {
//        throw new NotImplementedException();
//    }
//}

//public interface ISomeDependency
//{
//    Task<string> GetValueAsync();
//}

//public class ConnectionStringLoader
//{
//    private readonly ISomeDependency _someDependency;

//    public ConnectionStringLoader(ISomeDependency someDependency)
//    {
//        _someDependency = someDependency;
//    }

//    public async Task<string> GetConnectionString(string configurationName)
//    {
//        switch (configurationName)
//        {
//            case "A":
//                //Load value from other location
//                return await _someDependency.GetValueAsync();
//            case "B":
//                //Build string dynamically
//                return $"mongodb://localhost:27017/Tharga_{Environment.MachineName}{{part}}";
//            case "C":
//                //Use IConfiguration
//                return null;
//            default:
//                throw new ArgumentOutOfRangeException($"Unknown configurationName '{configurationName}'.");
//        }
//    }
//}