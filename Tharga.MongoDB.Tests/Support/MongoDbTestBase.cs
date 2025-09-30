using System;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Moq;
using Tharga.MongoDB.Atlas;
using Tharga.MongoDB.Configuration;
using Tharga.MongoDB.Internals;

namespace Tharga.MongoDB.Tests.Support;

public abstract class MongoDbTestBase : IDisposable
{
    private readonly Mock<IRepositoryConfigurationInternal> _configurationMock;
    private readonly DatabaseContext _databaseContext;
    private readonly IMongoDbServiceFactory _mongoDbServiceFactory;

    protected MongoDbTestBase()
    {
        _databaseContext = Mock.Of<DatabaseContext>(x => x.DatabasePart == Guid.NewGuid().ToString());
        _configurationMock = new Mock<IRepositoryConfigurationInternal>(MockBehavior.Strict);
        _configurationMock.Setup(x => x.GetDatabaseUrl()).Returns(() => new MongoUrl($"mongodb://localhost:27017/Tharga_MongoDb_Test_{_databaseContext.DatabasePart}"));
        _configurationMock.Setup(x => x.GetConfiguration()).Returns(Mock.Of<MongoDbConfig>(x => x.ResultLimit == 100));
        _configurationMock.Setup(x => x.GetExecuteInfoLogLevel()).Returns(LogLevel.Debug);
        _configurationMock.Setup(x => x.ShouldAssureIndex()).Returns(true);

        var configurationLoaderMock = new Mock<IRepositoryConfigurationLoader>(MockBehavior.Strict);
        configurationLoaderMock.Setup(x => x.GetConfiguration(It.IsAny<Func<DatabaseContext>>())).Returns(_configurationMock.Object);
        var loggerMock = new Mock<ILogger<MongoDbServiceFactory>>();

        var mongoDbFirewallStateService = new Mock<IMongoDbFirewallStateService>(MockBehavior.Strict);

        _mongoDbServiceFactory = new MongoDbServiceFactory(configurationLoaderMock.Object, mongoDbFirewallStateService.Object, loggerMock.Object);
    }

    protected IMongoDbServiceFactory MongoDbServiceFactory => _mongoDbServiceFactory;
    protected DatabaseContext DatabaseContext => _databaseContext;

    public void Dispose()
    {
        var databaseName = _configurationMock.Object.GetDatabaseUrl().DatabaseName;
        _mongoDbServiceFactory.GetMongoDbService(() => null).DropDatabase(databaseName);
    }
}