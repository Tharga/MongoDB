using System;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using Tharga.MongoDB.Atlas;
using Tharga.MongoDB.Configuration;
using Tharga.MongoDB.Internals;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests;

public class ServerDisplayNameTests
{
    [Theory]
    [InlineData("mongodb+srv://user:secret@cluster0.abc.mongodb.net/MyDb?retryWrites=true&w=majority", "cluster0.abc.mongodb.net")]
    [InlineData("mongodb://user:secret@host-a:27017,host-b:27017/MyDb?replicaSet=rs0", "host-a:27017,host-b:27017")]
    [InlineData("mongodb://user:p%40ss%2Fword@localhost:27017/MyDb", "localhost:27017")]
    [InlineData("user:secret@cluster0.abc.mongodb.net", "cluster0.abc.mongodb.net")]
    [InlineData("user:secret@host:27017/MyDb?appName=a@b", "host:27017")]
    [InlineData("mongodb://localhost:27017/MyDb", "localhost:27017")]
    [InlineData("localhost:27017", "localhost:27017")]
    [InlineData("remote-server", "remote-server")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void From_KeepsOnlyTheHostList(string urlOrServer, string expected)
    {
        //Act
        var result = ServerDisplayName.From(urlOrServer);

        //Assert
        result.Should().Be(expected);
    }

    [Fact]
    public async Task CollectionAccessEvent_FromConnectionStringWithCredentials_CarriesNoCredentials()
    {
        //Arrange
        var configuration = new Mock<IRepositoryConfigurationInternal>(MockBehavior.Strict);
        configuration.Setup(x => x.GetDatabaseUrl()).Returns(new MongoUrl("mongodb://user:secret@localhost:27017/Tharga_MongoDb_ServerDisplayName"));
        configuration.Setup(x => x.GetDatabaseContext()).Returns(new DatabaseContext());
        configuration.Setup(x => x.GetConfigurationName()).Returns("Default");
        var clientProvider = new Mock<IMongoDbClientProvider>(MockBehavior.Strict);
        clientProvider.Setup(x => x.GetClient(It.IsAny<MongoUrl>())).Returns((MongoUrl url) => new MongoClient(MongoClientSettings.FromUrl(url)));
        var sut = new MongoDbService(configuration.Object, Mock.Of<IMongoDbFirewallStateService>(), clientProvider.Object, Mock.Of<IExecuteLimiter>(), Mock.Of<ICollectionPool>(), Mock.Of<IInitiationLibrary>(), NullLogger.Instance);
        string server = null;
        sut.CollectionAccessEvent += (_, e) => server = e.Server;

        //Act
        await sut.GetCollectionAsync<TestEntity>("Anything");

        //Assert
        server.Should().Be("localhost");
    }

    [Fact]
    public void BsonToCollectionInfo_PersistedServerWithCredentials_IsStripped()
    {
        //Arrange
        var doc = new BsonDocument
        {
            { "_id", "MyDb/MyCol" },
            { "DatabaseName", "MyDb" },
            { "CollectionName", "MyCol" },
            { "Server", "user:secret@cluster0.abc.mongodb.net" },
        };

        //Act
        var result = MongoDbCollectionCache.BsonToCollectionInfo(doc, "Default");

        //Assert
        result.Server.Should().Be("cluster0.abc.mongodb.net");
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task LoadAsync_PersistedServerWithCredentials_IsRewrittenWithoutThem()
    {
        //Arrange
        var database = new MongoClient("mongodb://localhost:27017").GetDatabase($"Tharga_MongoDb_Test_{Guid.NewGuid()}");
        try
        {
            var monitor = database.GetCollection<BsonDocument>("_monitor");
            var persisted = MongoDbCollectionCache.CollectionInfoToBson("MyDb/MyCol", new CollectionInfo
            {
                ConfigurationName = "Default",
                DatabaseName = "MyDb",
                CollectionName = "MyCol",
                Server = "user:secret@cluster0.abc.mongodb.net",
                Registration = Registration.Static,
                EntityTypes = [],
                CollectionType = null,
            });
            await monitor.InsertOneAsync(persisted);
            var sut = CreateCache(database);

            //Act
            await sut.LoadAsync();

            //Assert
            sut.GetAll().Single().Server.Should().Be("cluster0.abc.mongodb.net");
            var stored = await monitor.Find(FilterDefinition<BsonDocument>.Empty).SingleAsync();
            stored["Server"].AsString.Should().Be("cluster0.abc.mongodb.net");
        }
        finally
        {
            await database.Client.DropDatabaseAsync(database.DatabaseNamespace.DatabaseName);
        }
    }

    private static MongoDbCollectionCache CreateCache(IMongoDatabase database)
    {
        var service = new Mock<IMongoDbService>();
        service.Setup(x => x.AssureFirewallAccessAsync(It.IsAny<bool>())).Returns(ValueTask.FromResult<string>(null));
        service.As<IMongoDbServiceInternal>().Setup(x => x.BaseMongoDatabase).Returns(database);
        var factory = new Mock<IMongoDbServiceFactory>();
        factory.Setup(x => x.GetMongoDbService(It.IsAny<Func<DatabaseContext>>())).Returns(service.Object);
        var repositoryConfiguration = new Mock<IRepositoryConfiguration>();
        repositoryConfiguration.Setup(x => x.GetDatabaseConfigurationNames()).Returns(["Default"]);

        return new MongoDbCollectionCache(factory.Object, repositoryConfiguration.Object, Options.Create(new DatabaseOptions()), NullLogger<MongoDbCollectionCache>.Instance);
    }
}
