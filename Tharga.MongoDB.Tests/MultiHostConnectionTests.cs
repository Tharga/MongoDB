using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using Tharga.MongoDB.Atlas;
using Tharga.MongoDB.Configuration;
using Tharga.MongoDB.Disk;
using Tharga.MongoDB.Internals;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests;

public class MultiHostConnectionTests
{
    private const string MultiHostUrl = "mongodb://user:secret@h1.example:27017,h2.example:27018/Tharga_MultiHost?replicaSet=rs0";
    private const string CosmosUrl = "mongodb://acct:key@acct.mongo.cosmos.azure.com:10255/Tharga_MultiHost?ssl=true&replicaSet=globaldb";

    [Fact]
    public async Task GetCollectionAsync_MultiHostConnectionString_DoesNotThrow()
    {
        //Arrange
        var sut = CreateService(MultiHostUrl, out _);

        //Act
        var act = () => sut.GetCollectionAsync<TestEntity>("Anything");

        //Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void HostNameAndAddress_MultiHostConnectionString_ListEveryServer()
    {
        //Arrange
        var sut = CreateService(MultiHostUrl, out _);

        //Act
        var hostName = sut.GetDatabaseHostName();
        var address = sut.GetDatabaseAddress();

        //Assert
        hostName.Should().Be("h1.example,h2.example");
        address.Should().Be("h1.example:27017,h2.example:27018");
    }

    [Fact]
    public void HostNameAndAddress_SingleHost_AreUnchanged()
    {
        //Arrange
        var sut = CreateService(CosmosUrl, out _);

        //Act
        var hostName = sut.GetDatabaseHostName();
        var address = sut.GetDatabaseAddress();

        //Assert
        hostName.Should().Be("acct.mongo.cosmos.azure.com");
        address.Should().Be("acct.mongo.cosmos.azure.com:10255");
    }

    [Theory]
    [InlineData("mongodb://localhost:27017,localhost:27018/Tharga_MultiHost?replicaSet=rs0", false)]
    [InlineData("mongodb://localhost:27017,h2.example:27018/Tharga_MultiHost?replicaSet=rs0", true)]
    [InlineData(MultiHostUrl, true)]
    [InlineData("mongodb://localhost:27017/Tharga_MultiHost", false)]
    public async Task AssureFirewallAccessAsync_ChecksTheFirewallUnlessEveryHostIsLocal(string url, bool expectFirewallCheck)
    {
        //Arrange
        var sut = CreateService(url, out var firewall);

        //Act
        await sut.AssureFirewallAccessAsync();

        //Assert
        firewall.Verify(x => x.AssureFirewallAccessAsync(It.IsAny<MongoDbApiAccess>(), It.IsAny<bool>()), expectFirewallCheck ? Times.Once() : Times.Never());
    }

    [Theory]
    [InlineData("mongodb://localhost:27017/Tharga_MultiHost")]
    [InlineData(CosmosUrl)]
    [InlineData(MultiHostUrl)]
    public async Task ServerName_IsTheServerTheMonitorRecords(string url)
    {
        //Arrange
        var service = CreateService(url, out _);
        string recorded = null;
        service.CollectionAccessEvent += (_, e) => recorded = e.Server;
        var collection = new ServerNameCollection(CreateFactory(service));

        //Act
        await service.GetCollectionAsync<TestEntity>("Anything");

        //Assert
        collection.ServerName.Should().Be(recorded);
    }

    [Fact]
    public async Task GetCollectionsWithFailedIndices_CosmosStyleServer_FindsTheFailure()
    {
        //Arrange
        var service = CreateService(CosmosUrl, out _);
        string recorded = null;
        service.CollectionAccessEvent += (_, e) => recorded = e.Server;
        await service.GetCollectionAsync<TestEntity>("Orders");
        var collection = new ServerNameCollection(CreateFactory(service));
        var initiationLibrary = new InitiationLibrary();
        initiationLibrary.ShouldInitiate(collection.ServerName, "Tharga_MultiHost", "Orders");
        initiationLibrary.AddFailedInitiateIndex(collection.ServerName, "Tharga_MultiHost", "Orders", IndexFailOperation.Create, "Ix", "duplicate key");
        var info = new CollectionInfo
        {
            ConfigurationName = "Default",
            DatabaseName = "Tharga_MultiHost",
            CollectionName = "Orders",
            Server = recorded,
            Registration = Registration.Static,
            EntityTypes = [],
            CollectionType = null,
        };
        var sut = CreateMonitor(initiationLibrary, info);

        //Act
        var result = sut.GetCollectionsWithFailedIndices();

        //Assert
        result.Should().ContainSingle().Which.CollectionName.Should().Be("Orders");
    }

    private static MongoDbService CreateService(string url, out Mock<IMongoDbFirewallStateService> firewall)
    {
        var configuration = new Mock<IRepositoryConfigurationInternal>();
        configuration.Setup(x => x.GetDatabaseUrl()).Returns(new MongoUrl(url));
        configuration.Setup(x => x.GetDatabaseContext()).Returns(new DatabaseContext());
        configuration.Setup(x => x.GetConfigurationName()).Returns("Default");
        configuration.Setup(x => x.GetConfiguration()).Returns(new MongoDbConfig { AccessInfo = new MongoDbApiAccess() });
        var clientProvider = new Mock<IMongoDbClientProvider>();
        clientProvider.Setup(x => x.GetClient(It.IsAny<MongoUrl>())).Returns((MongoUrl mongoUrl) => new MongoClient(MongoClientSettings.FromUrl(mongoUrl)));
        firewall = new Mock<IMongoDbFirewallStateService>();
        firewall.Setup(x => x.AssureFirewallAccessAsync(It.IsAny<MongoDbApiAccess>(), It.IsAny<bool>())).Returns(ValueTask.FromResult("Firewall checked."));
        return new MongoDbService(configuration.Object, firewall.Object, clientProvider.Object, Mock.Of<IExecuteLimiter>(), Mock.Of<ICollectionPool>(), Mock.Of<IInitiationLibrary>(), NullLogger.Instance);
    }

    private static IMongoDbServiceFactory CreateFactory(IMongoDbService service)
    {
        var factory = new Mock<IMongoDbServiceFactory>();
        factory.Setup(x => x.GetMongoDbService(It.IsAny<Func<DatabaseContext>>())).Returns(service);
        return factory.Object;
    }

    private static DatabaseMonitor CreateMonitor(IInitiationLibrary initiationLibrary, CollectionInfo cached)
    {
        var factory = new Mock<IMongoDbServiceFactory>();
        factory.Setup(f => f.SourceName).Returns("TestServer/TestApp");
        var instance = new Mock<IMongoDbInstance>();
        instance.Setup(i => i.RegisteredCollections).Returns(new ConcurrentDictionary<Type, Type>());
        var serviceProvider = new ServiceCollection().AddSingleton(initiationLibrary).BuildServiceProvider();
        var cache = new Mock<ICollectionCache>();
        cache.Setup(c => c.LoadAsync()).Returns(Task.CompletedTask);
        cache.Setup(c => c.GetKeys()).Returns([cached.Key]);
        cache.Setup(c => c.GetAll()).Returns([cached]);
        var options = Options.Create(new DatabaseOptions { Monitor = new MonitorOptions() });

        var monitor = new DatabaseMonitor(
            factory.Object,
            instance.Object,
            serviceProvider,
            new Mock<IRepositoryConfiguration>().Object,
            new Mock<ICollectionProvider>().Object,
            new CallLibrary(options),
            cache.Object,
            new Mock<IQueueMonitor>().Object,
            new ConnectionPoolMonitor(),
            options,
            NullLogger<DatabaseMonitor>.Instance);
        monitor.Start(serviceProvider);
        return monitor;
    }

    private sealed class ServerNameCollection : DiskRepositoryCollectionBase<TestEntity, ObjectId>
    {
        public ServerNameCollection(IMongoDbServiceFactory factory)
            : base(factory)
        {
        }
    }
}
