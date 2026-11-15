using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using Moq;
using Tharga.MongoDB.Configuration;
using Tharga.MongoDB.Internals;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests;

public class Issue185SchemaDriftTests
{
    private interface IUnrelatedGeneric<T>;

    private abstract class CollectionWithUnrelatedInterface : Disk.DiskRepositoryCollectionBase<TestEntity, ObjectId>, IUnrelatedGeneric<string>
    {
        protected CollectionWithUnrelatedInterface(IMongoDbServiceFactory factory, DatabaseContext databaseContext)
            : base(factory, null, databaseContext)
        {
        }
    }

    [Fact]
    public void ResolveRegisteredEntityType_UsesBaseClass_NotFirstGenericInterface()
    {
        var entityType = DatabaseMonitor.ResolveRegisteredEntityType(typeof(IUnrelatedGeneric<string>), typeof(CollectionWithUnrelatedInterface));

        entityType.Should().Be(typeof(TestEntity));
    }

    [Fact]
    public void ResolveRegisteredEntityType_ReturnsNull_WhenNothingDerivesFromRepositoryCollectionBase()
    {
        var entityType = DatabaseMonitor.ResolveRegisteredEntityType(typeof(IUnrelatedGeneric<string>), typeof(List<string>));

        entityType.Should().BeNull();
    }

    [Fact]
    public async Task GetInstanceAsync_EnrichesCachedEntry_WithCleanInfoFromDatabase()
    {
        const string key = "Default.TestDb.TestCol";
        var cached = new CollectionInfo
        {
            ConfigurationName = "Default",
            DatabaseName = "TestDb",
            CollectionName = "TestCol",
            Server = "localhost:27017",
            Discovery = Discovery.Database,
            Registration = Registration.NotInCode,
            EntityTypes = [],
            CollectionType = null,
        };
        var clean = new CleanInfo { SchemaFingerprint = "fp", CleanedAt = DateTime.UtcNow, DocumentsCleaned = 3 };

        var serviceMock = new Mock<IMongoDbService>();
        serviceMock.Setup(s => s.DoesCollectionExist("TestCol")).ReturnsAsync(true);
        serviceMock.Setup(s => s.ReadCleanInfoAsync("TestDb", "TestCol")).ReturnsAsync(clean);

        var factoryMock = new Mock<IMongoDbServiceFactory>();
        factoryMock.Setup(f => f.SourceName).Returns("Test/App");
        factoryMock.Setup(f => f.GetMongoDbService(It.IsAny<Func<DatabaseContext>>())).Returns(serviceMock.Object);

        var instanceMock = new Mock<IMongoDbInstance>();
        instanceMock.Setup(i => i.RegisteredCollections).Returns(new ConcurrentDictionary<Type, Type>());

        var repositoryConfigMock = new Mock<IRepositoryConfiguration>();
        repositoryConfigMock.Setup(r => r.GetDatabaseConfigurationNames()).Returns(["Default"]);

        var cacheMock = new Mock<ICollectionCache>();
        cacheMock.Setup(c => c.LoadAsync()).Returns(Task.CompletedTask);
        cacheMock.Setup(c => c.GetKeys()).Returns(Array.Empty<string>());
        cacheMock.Setup(c => c.GetAll()).Returns(Array.Empty<CollectionInfo>());
        var cachedRef = cached;
        cacheMock.Setup(c => c.TryGet(key, out cachedRef)).Returns(true);

        var options = Options.Create(new DatabaseOptions { Monitor = new MonitorOptions() });
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var monitor = new DatabaseMonitor(
            factoryMock.Object,
            instanceMock.Object,
            serviceProvider,
            repositoryConfigMock.Object,
            new Mock<ICollectionProvider>().Object,
            new CallLibrary(options),
            cacheMock.Object,
            new Mock<IQueueMonitor>().Object,
            new ConnectionPoolMonitor(),
            options,
            NullLogger<DatabaseMonitor>.Instance);
        monitor.Start(serviceProvider);

        var result = await monitor.GetInstanceAsync(new CollectionFingerprint { ConfigurationName = "Default", DatabaseName = "TestDb", CollectionName = "TestCol" });

        result.Clean.Should().Be(clean);
        cacheMock.Verify(c => c.Set(key, It.Is<CollectionInfo>(i => i.Clean == clean)), Times.Once);
    }
}
