using System;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Moq;
using Moq.AutoMock;
using Tharga.MongoDB.Configuration;
using Tharga.MongoDB.Internals;

namespace Tharga.MongoDB.Tests.Lockable.Base;

public abstract class LockableTestBase : IDisposable
{
    private readonly Mock<IRepositoryConfigurationInternal> _configurationMock;
    internal readonly MongoDbServiceFactory _mongoDbServiceFactory;

    protected LockableTestBase()
    {
        var mocker = new AutoMocker();

        var databaseContext = Mock.Of<DatabaseContext>(x => x.DatabasePart == Guid.NewGuid().ToString());
        _configurationMock = new Mock<IRepositoryConfigurationInternal>(MockBehavior.Strict);
        _configurationMock.Setup(x => x.GetDatabaseUrl()).Returns(() => new MongoUrl($"mongodb://localhost:27017/Tharga_MongoDb_Test_{databaseContext.DatabasePart}"));
        _configurationMock.Setup(x => x.GetConfiguration()).Returns(Mock.Of<MongoDbConfig>(x => x.ResultLimit == 100));
        _configurationMock.Setup(x => x.GetExecuteInfoLogLevel()).Returns(LogLevel.Debug);
        _configurationMock.Setup(x => x.GetAssureIndexMode()).Returns(AssureIndexMode.ByName);
        _configurationMock.Setup(x => x.GetConfigurationName()).Returns("Default");
        _configurationMock.Setup(x => x.GetDatabaseContext()).Returns(Mock.Of<DatabaseContext>());

        var configurationLoaderMock = new Mock<IRepositoryConfigurationLoader>(MockBehavior.Strict);
        configurationLoaderMock.Setup(x => x.GetConfiguration(It.IsAny<Func<DatabaseContext>>())).Returns(_configurationMock.Object);
        mocker.Use(configurationLoaderMock.Object);

        var mongoDbClientProvider = new Mock<IMongoDbClientProvider>(MockBehavior.Strict);
        mongoDbClientProvider.Setup(x => x.GetClient(It.IsAny<MongoUrl>())).Returns((MongoUrl mongoUrl) =>
        {
            var settings = MongoClientSettings.FromUrl(mongoUrl);
            return new MongoClient(settings);
        });
        mocker.Use(mongoDbClientProvider);

        _mongoDbServiceFactory = mocker.CreateInstance<MongoDbServiceFactory>();
    }

    public void Dispose()
    {
        var databaseName = _configurationMock.Object.GetDatabaseUrl().DatabaseName;
        _mongoDbServiceFactory.GetMongoDbService(() => null).DropDatabase(databaseName);
    }
}