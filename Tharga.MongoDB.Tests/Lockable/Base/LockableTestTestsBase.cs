﻿using System;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Moq;
using Tharga.MongoDB.Atlas;
using Tharga.MongoDB.Configuration;
using Tharga.MongoDB.Internals;

namespace Tharga.MongoDB.Tests.Lockable.Base;

public abstract class LockableTestTestsBase
{
    private readonly Mock<IRepositoryConfigurationInternal> _configurationMock;
    private readonly DatabaseContext _databaseContext;
    internal readonly MongoDbServiceFactory _mongoDbServiceFactory;

    public LockableTestTestsBase()
    {
        _databaseContext = Mock.Of<DatabaseContext>(x => x.DatabasePart == Guid.NewGuid().ToString());
        _configurationMock = new Mock<IRepositoryConfigurationInternal>(MockBehavior.Strict);
        _configurationMock.Setup(x => x.GetDatabaseUrl()).Returns(() => new MongoUrl($"mongodb://localhost:27017/Tharga_MongoDb_Test_{_databaseContext.DatabasePart}"));
        _configurationMock.Setup(x => x.GetConfiguration()).Returns(Mock.Of<MongoDbConfig>(x => x.ResultLimit == 100));
        _configurationMock.Setup(x => x.GetExecuteInfoLogLevel()).Returns(LogLevel.Debug);

        var configurationLoaderMock = new Mock<IRepositoryConfigurationLoader>(MockBehavior.Strict);
        configurationLoaderMock.Setup(x => x.GetConfiguration(It.IsAny<Func<DatabaseContext>>())).Returns(_configurationMock.Object);
        var loggerMock = new Mock<ILogger<MongoDbServiceFactory>>();

        var mongoDbFirewallStateService = new Mock<IMongoDbFirewallStateService>(MockBehavior.Strict);

        _mongoDbServiceFactory = new MongoDbServiceFactory(configurationLoaderMock.Object, mongoDbFirewallStateService.Object, loggerMock.Object);
    }
}