using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tharga.MongoDB.ConsoleSample.SampleRepo;

namespace Tharga.MongoDB.ConsoleSample;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        var services = new ServiceCollection();
        services.AddMongoDB(o =>
        {
            o.ConnectionStringLoader = _ => "mongodb://localhost:27017/Tharga_MongoDB_ConsoleSample{part}";
            o.ActionEvent = data => { Console.WriteLine($"---> {data.Action.Message}"); };
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