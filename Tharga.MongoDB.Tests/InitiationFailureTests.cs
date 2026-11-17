using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using Moq.AutoMock;
using Tharga.MongoDB.Configuration;
using Tharga.MongoDB.Disk;
using Tharga.MongoDB.Internals;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests;

/// <summary>
/// Coverage for GitHub issue #202. A failure during collection initiation left the collection marked as initiated,
/// so the next access skipped InitAsync and clean-on-startup for the rest of the process.
/// </summary>
[Collection("Sequential")]
public class InitiationFailureTests : IDisposable
{
    private readonly Mock<IRepositoryConfigurationInternal> _configurationMock;
    private readonly IMongoDbServiceFactory _mongoDbServiceFactory;
    private readonly DatabaseContext _databaseContext;

    public InitiationFailureTests()
    {
        var mocker = new AutoMocker(MockBehavior.Strict);

        _databaseContext = Mock.Of<DatabaseContext>(x => x.DatabasePart == Guid.NewGuid().ToString() && x.ConfigurationName == "Default");
        _configurationMock = new Mock<IRepositoryConfigurationInternal>(MockBehavior.Strict);
        _configurationMock.Setup(x => x.GetDatabaseUrl()).Returns(() => new MongoUrl($"mongodb://localhost:27017/Tharga_MongoDb_Test_{_databaseContext.DatabasePart}"));
        _configurationMock.Setup(x => x.GetConfiguration()).Returns(Mock.Of<MongoDbConfig>(x => x.FetchSize == 100));
        _configurationMock.Setup(x => x.GetAssureIndexMode()).Returns(AssureIndexMode.ByName);
        _configurationMock.Setup(x => x.GetConfigurationName()).Returns("Default");
        _configurationMock.Setup(x => x.GetDatabaseContext()).Returns(Mock.Of<DatabaseContext>());

        var configurationLoaderMock = new Mock<IRepositoryConfigurationLoader>(MockBehavior.Strict);
        configurationLoaderMock.Setup(x => x.GetConfiguration(It.IsAny<Func<DatabaseContext>>())).Returns(_configurationMock.Object);
        mocker.Use(configurationLoaderMock);

        var mongoDbClientProvider = new Mock<IMongoDbClientProvider>(MockBehavior.Strict);
        mongoDbClientProvider.Setup(x => x.GetClient(It.IsAny<MongoUrl>())).Returns((MongoUrl mongoUrl) => new MongoClient(MongoClientSettings.FromUrl(mongoUrl)));
        mocker.Use(mongoDbClientProvider);

        mocker.Use((IExecuteLimiter)new ExecuteLimiter(Mock.Of<IOptions<ExecuteLimiterOptions>>(x => x.Value == new ExecuteLimiterOptions()), null));
        mocker.Use<ICollectionPool>(new CollectionPool());
        mocker.Use<IInitiationLibrary>(new InitiationLibrary());

        _mongoDbServiceFactory = mocker.CreateInstance<MongoDbServiceFactory>();
    }

    public void Dispose()
    {
        var databaseName = _configurationMock.Object.GetDatabaseUrl().DatabaseName;
        _mongoDbServiceFactory.GetMongoDbService(() => null).DropDatabase(databaseName);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task ThrowingInitAsync_IsRunAgainOnNextAccess()
    {
        var initCount = 0;
        var sut = new FailingInitCollection(_mongoDbServiceFactory, _databaseContext, "InitFailure", () =>
        {
            if (Interlocked.Increment(ref initCount) == 1) throw new InvalidOperationException("init failed");
        });

        var first = () => sut.CountAsync(x => true);
        await first.Should().ThrowAsync<InvalidOperationException>();

        await sut.CountAsync(x => true);
        initCount.Should().Be(2, "the failed initiation must be retried");

        await sut.CountAsync(x => true);
        initCount.Should().Be(2, "a successful initiation is not repeated");
    }

    private sealed class FailingInitCollection : DiskRepositoryCollectionBase<TestEntity, ObjectId>
    {
        private readonly string _collectionName;
        private readonly Action _onInit;

        public FailingInitCollection(IMongoDbServiceFactory factory, DatabaseContext context, string collectionName, Action onInit)
            : base(factory, null, context)
        {
            _collectionName = collectionName;
            _onInit = onInit;
        }

        public override string CollectionName => _collectionName;

        protected override Task InitAsync(IMongoCollection<TestEntity> collection)
        {
            _onInit();
            return Task.CompletedTask;
        }
    }
}
