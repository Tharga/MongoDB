using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using Moq.AutoMock;
using Tharga.MongoDB.Configuration;
using Tharga.MongoDB.Internals;
using Tharga.MongoDB.Lockable;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests.Lockable;

/// <summary>
/// A transactional lease commit on a collection whose database name is templated with {part}. The other
/// lockable tests all run against a single database, so they never exercise the path where the transaction's
/// session has to be resolved for the same database the writes go to.
/// </summary>
[Collection("Sequential")]
public class TransactionDatabasePartTests : IDisposable
{
    private const string DatabasePartValue = "farm1";

    private readonly string _databaseBaseName = $"Tharga_MongoDb_Test_Part_{Guid.NewGuid():N}";
    private readonly MongoDbServiceFactory _mongoDbServiceFactory;

    public TransactionDatabasePartTests()
    {
        var mocker = new AutoMocker(MockBehavior.Strict);

        var configurationLoaderMock = new Mock<IRepositoryConfigurationLoader>(MockBehavior.Strict);
        configurationLoaderMock
            .Setup(x => x.GetConfiguration(It.IsAny<Func<DatabaseContext>>()))
            .Returns((Func<DatabaseContext> contextLoader) => BuildConfiguration(contextLoader()));
        mocker.Use(configurationLoaderMock.Object);

        var mongoDbClientProvider = new Mock<IMongoDbClientProvider>(MockBehavior.Strict);
        mongoDbClientProvider
            .Setup(x => x.GetClient(It.IsAny<MongoUrl>()))
            .Returns((MongoUrl mongoUrl) => new MongoClient(MongoClientSettings.FromUrl(mongoUrl)));
        mocker.Use(mongoDbClientProvider);

        var executeLimiter = new ExecuteLimiter(Mock.Of<IOptions<ExecuteLimiterOptions>>(x => x.Value == new ExecuteLimiterOptions()), null);
        mocker.Use((IExecuteLimiter)executeLimiter);
        mocker.Use(new Mock<ICollectionPool>(MockBehavior.Loose));
        mocker.Use(new Mock<IInitiationLibrary>(MockBehavior.Loose));

        _mongoDbServiceFactory = mocker.CreateInstance<MongoDbServiceFactory>();
    }

    private IRepositoryConfigurationInternal BuildConfiguration(DatabaseContext databaseContext)
    {
        // Mirrors a connection string templated with {part}: without the part there is no database to talk to.
        var part = databaseContext?.DatabasePart;
        var databaseName = string.IsNullOrEmpty(part) ? string.Empty : $"{_databaseBaseName}_{part}";

        var configurationMock = new Mock<IRepositoryConfigurationInternal>(MockBehavior.Strict);
        configurationMock.Setup(x => x.GetDatabaseUrl()).Returns(() => new MongoUrl($"mongodb://localhost:27017/{databaseName}"));
        configurationMock.Setup(x => x.GetConfiguration()).Returns(Mock.Of<MongoDbConfig>(x => x.FetchSize == 100));
        configurationMock.Setup(x => x.GetAssureIndexMode()).Returns(AssureIndexMode.ByName);
        configurationMock.Setup(x => x.GetConfigurationName()).Returns("Default");
        configurationMock.Setup(x => x.GetDatabaseContext()).Returns(databaseContext);
        return configurationMock.Object;
    }

    [Fact]
    [Trait("Category", "Database")]
    [Trait("Category", "RequiresReplicaSet")]
    public async Task TransactionalCommit_OnACollectionWithADatabasePart_CommitsToThatDatabase()
    {
        var sut = new PartedLockableTestRepositoryCollection(_mongoDbServiceFactory);
        var first = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "first" };
        var second = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "second" };
        await sut.AddAsync(first);
        await sut.AddAsync(second);

        await using var lease = await sut.LockManyAsync([first.Id, second.Id]);
        lease.MarkForUpdate(lease.Documents.Single(x => x.Id == first.Id) with { Data = "first-updated" });
        lease.MarkForDelete(second.Id);

        var summary = await lease.CommitAsync(transactional: true);

        summary.Updated.Should().Be(1);
        summary.Deleted.Should().Be(1);
        summary.Failures.Should().BeEmpty();

        var post = await sut.GetOneAsync(first.Id);
        post.Data.Should().Be("first-updated");
        post.Lock.Should().BeNull();
        (await sut.GetOneAsync(second.Id)).Should().BeNull();
    }

    public void Dispose()
    {
        var service = _mongoDbServiceFactory.GetMongoDbService(() => new DatabaseContext { DatabasePart = DatabasePartValue });
        service.DropDatabase($"{_databaseBaseName}_{DatabasePartValue}");
    }

    private class PartedLockableTestRepositoryCollection : LockableRepositoryCollectionBase<LockableTestEntity, ObjectId>
    {
        public PartedLockableTestRepositoryCollection(IMongoDbServiceFactory mongoDbServiceFactory)
            : base(mongoDbServiceFactory)
        {
        }

        public override string DatabasePart => DatabasePartValue;

        protected override bool RequireActor => false;
    }
}
