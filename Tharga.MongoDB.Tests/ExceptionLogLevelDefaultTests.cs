using System;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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

public class ExceptionLogLevelDefaultTests
{
    [Theory]
    [InlineData(typeof(TimeoutException), LogLevel.Warning)]
    [InlineData(typeof(InvalidOperationException), LogLevel.Error)]
    public void DefaultLevel_KeepsTheEarlierMapping(Type exceptionType, LogLevel expected)
    {
        //Arrange
        var sut = new DefaultLevelCollection(CreateFactoryWithoutServer());

        //Act
        var result = sut.DefaultLevel((Exception)Activator.CreateInstance(exceptionType));

        //Assert
        result.Should().Be(expected);
    }

    private static IMongoDbServiceFactory CreateFactoryWithoutServer()
    {
        var configuration = new Mock<IRepositoryConfigurationInternal>();
        configuration.Setup(x => x.GetDatabaseUrl()).Returns(new MongoUrl("mongodb://localhost:27017/Tharga_MongoDb_DefaultLevel"));
        var clientProvider = new Mock<IMongoDbClientProvider>();
        clientProvider.Setup(x => x.GetClient(It.IsAny<MongoUrl>())).Returns((MongoUrl url) => new MongoClient(MongoClientSettings.FromUrl(url)));
        var service = new MongoDbService(configuration.Object, Mock.Of<IMongoDbFirewallStateService>(), clientProvider.Object, Mock.Of<IExecuteLimiter>(), Mock.Of<ICollectionPool>(), Mock.Of<IInitiationLibrary>(), NullLogger.Instance);
        var factory = new Mock<IMongoDbServiceFactory>();
        factory.Setup(x => x.GetMongoDbService(It.IsAny<Func<DatabaseContext>>())).Returns(service);
        return factory.Object;
    }

    private sealed class DefaultLevelCollection : DiskRepositoryCollectionBase<TestEntity, ObjectId>
    {
        public DefaultLevelCollection(IMongoDbServiceFactory factory)
            : base(factory)
        {
        }

        public LogLevel DefaultLevel(Exception exception) => GetExceptionLogLevel(exception, nameof(DefaultLevel));
    }
}
