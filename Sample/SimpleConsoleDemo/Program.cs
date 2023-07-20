using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using SimpleConsoleDemo;
using Tharga.MongoDB;
using Tharga.MongoDB.Configuration;
using Tharga.MongoDB.Disk;

var services = new ServiceCollection();
services.AddMongoDB(o =>
{
    o.ConnectionStringLoader = (_,_) => Task.FromResult<ConnectionString>("mongodb://localhost:27017/SimpleDemo");
    o.ActionEvent = e => { Console.WriteLine((string?)e.Action.Message); };
});

var serviceProvider = services.BuildServiceProvider();

var simpleRepo = serviceProvider.GetService<MySimpleRepo>();
await simpleRepo!.AddAsync(new MyEntity());
var oneItem = await simpleRepo.GetFirstOrDefaultAsync();

Console.WriteLine($"Got item with id '{oneItem.Id}' from the database.");

namespace SimpleConsoleDemo
{
    public class MySimpleRepo : DiskRepositoryCollectionBase<MyEntity, ObjectId>
    {
        public Task<MyEntity> GetFirstOrDefaultAsync()
        {
            return base.GetOneAsync(x => true);
        }

        public MySimpleRepo(IMongoDbServiceFactory mongoDbServiceFactory)
            : base(mongoDbServiceFactory)
        {
        }
    }

    public record MyEntity : EntityBase<ObjectId>
    {
    }
}