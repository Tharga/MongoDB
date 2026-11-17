using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using Moq.AutoMock;
using System.Linq;
using Tharga.MongoDB.Configuration;
using Tharga.MongoDB.Disk;
using Tharga.MongoDB.Internals;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests;

/// <summary>
/// Coverage for GitHub issue #199. GetManyProjectionAsync threw ResultLimitException once more than FetchSize documents were read,
/// where FetchSize is the auto-computed batch-size estimate (minimum 100) when nothing is configured. GetManyAsync has no such check.
/// </summary>
[Collection("Sequential")]
public class GetManyProjectionLimitTests : IDisposable
{
    private readonly Mock<IRepositoryConfigurationInternal> _configurationMock;
    private readonly IMongoDbServiceFactory _mongoDbServiceFactory;
    private readonly DatabaseContext _databaseContext;

    public GetManyProjectionLimitTests()
    {
        var mocker = new AutoMocker(MockBehavior.Strict);

        _databaseContext = Mock.Of<DatabaseContext>(x => x.DatabasePart == Guid.NewGuid().ToString() && x.ConfigurationName == "Default");
        _configurationMock = new Mock<IRepositoryConfigurationInternal>(MockBehavior.Strict);
        _configurationMock.Setup(x => x.GetDatabaseUrl()).Returns(() => new MongoUrl($"mongodb://localhost:27017/Tharga_MongoDb_Test_{_databaseContext.DatabasePart}"));
        _configurationMock.Setup(x => x.GetConfiguration()).Returns(Mock.Of<MongoDbConfig>());
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

    private async Task<ProjectionCollection> CreateAsync(string name, int? configuredFetchSize, int count)
    {
        // First access on an empty collection, so the auto fetch size is the minimum of 100.
        var sut = new ProjectionCollection(_mongoDbServiceFactory, _databaseContext, name, configuredFetchSize);
        await sut.CountAsync(x => true);
        await sut.AddManyAsync(Enumerable.Range(0, count).Select(i => new TestEntity { Id = ObjectId.GenerateNewId(), Value = i.ToString() }));
        return sut;
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task AutoFetchSize_LimitAboveAutoSize_ReturnsTheLimitedItems()
    {
        var sut = await CreateAsync("ProjectionAuto", null, 500);
        sut.FetchSize.Should().Be(100, "the auto fetch size of an empty collection is the minimum");

        var result = await sut.GetManyProjectionAsync<TestProjectionEntity>(x => true, new Options<TestEntity> { Limit = 500 });

        result.Items.Should().HaveCount(500);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task ConfiguredFetchSize_LimitAboveFetchSize_ReturnsTheLimitedItems()
    {
        var sut = await CreateAsync("ProjectionConfigured", 100, 500);

        var result = await sut.GetManyProjectionAsync<TestProjectionEntity>(x => true, new Options<TestEntity> { Limit = 500 });

        result.Items.Should().HaveCount(500);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task NoLimit_IsBoundedByTheDefaultLimit()
    {
        var sut = await CreateAsync("ProjectionNoLimit", null, 250);

        var result = await sut.GetManyProjectionAsync<TestProjectionEntity>(x => true);

        result.Items.Should().HaveCount(100);
        result.TotalCount.Should().Be(250);
    }

    private sealed class ProjectionCollection : DiskRepositoryCollectionBase<TestEntity, ObjectId>
    {
        private readonly string _collectionName;
        private readonly int? _fetchSize;

        public ProjectionCollection(IMongoDbServiceFactory factory, DatabaseContext context, string collectionName, int? fetchSize)
            : base(factory, null, context)
        {
            _collectionName = collectionName;
            _fetchSize = fetchSize;
        }

        public override string CollectionName => _collectionName;
        public override int? FetchSize => _fetchSize ?? base.FetchSize;
    }
}
